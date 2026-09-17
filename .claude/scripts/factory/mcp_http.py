#!/usr/bin/env python
"""mcp_http.py — MCP for Unity over plain HTTP, for when a session's UnityMCP connector is dead.

A Claude Desktop session dials `.mcp.json`'s UnityMCP once at session start; if 127.0.0.1:8080
was silent at that second the connector stays `failed` for the session's whole life and cannot
be re-dialled (reconnect_session_connector refuses project servers). The server itself does not
care who calls it. This script speaks the Streamable HTTP MCP protocol with the standard
library only, so a shift, a sweeper or a red team can use the bridge from Bash regardless of
the connector's state (docs/loop/CHARTER.md D7; LOOP-STATE § RIG).

Usage (from the clone root; exit 0 ok, 1 tool error, 2 no server):
  python .claude/scripts/factory/mcp_http.py ping                 -> {"ok": true, "session": ...}
  python .claude/scripts/factory/mcp_http.py instances            -> the server's connected Editors
  python .claude/scripts/factory/mcp_http.py tools                -> tool names
  python .claude/scripts/factory/mcp_http.py resource <uri>       -> a resource's text
  python .claude/scripts/factory/mcp_http.py call <tool> '<json>' -> tools/call; prints the JSON result
  python .claude/scripts/factory/mcp_http.py code '<C# ...>'      -> execute_code shorthand
  python .claude/scripts/factory/mcp_http.py wait-instance <name> [seconds]
                                                                  -> exit 0 once an Editor whose name
                                                                     starts with <name> is connected

Set MCP_HTTP_URL to target another endpoint (default http://127.0.0.1:8080/mcp).
"""
import io, json, os, sys, time, urllib.request, urllib.error

DEFAULT_URL = "http://127.0.0.1:8080/mcp"
PROTOCOL = "2025-03-26"
CLIENT = {"name": "robogame-factory-mcp-http", "version": "0.1"}


def url():
    return os.environ.get("MCP_HTTP_URL", DEFAULT_URL)


def parse_body(text):
    """Both framings the server may answer with: SSE `data:` lines, or a bare JSON object."""
    msgs = []
    for line in text.splitlines():
        line = line.strip()
        if line.startswith("data:"):
            payload = line[5:].strip()
            if payload:
                msgs.append(json.loads(payload))
        elif line.startswith("{"):
            msgs.append(json.loads(line))
    return msgs


def headers(session=None):
    h = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream"}
    if session:
        h["mcp-session-id"] = session
    return h


def post(body, session=None, timeout=90, opener=urllib.request.urlopen):
    """POST one JSON-RPC message; return (session-id header, parsed messages)."""
    req = urllib.request.Request(url(), data=json.dumps(body).encode("utf-8"),
                                 headers=headers(session), method="POST")
    with opener(req, timeout=timeout) as r:
        sid = r.headers.get("mcp-session-id")
        text = r.read().decode("utf-8", "replace")
    return sid, parse_body(text)


def open_session(opener=urllib.request.urlopen):
    sid, msgs = post({"jsonrpc": "2.0", "id": 1, "method": "initialize",
                      "params": {"protocolVersion": PROTOCOL, "capabilities": {}, "clientInfo": CLIENT}},
                     opener=opener)
    if not any(m.get("id") == 1 and "result" in m for m in msgs):
        raise RuntimeError("initialize got no result: %r" % (msgs[:1],))
    try:
        post({"jsonrpc": "2.0", "method": "notifications/initialized"}, sid, timeout=10, opener=opener)
    except urllib.error.HTTPError:
        pass  # a bare notification may be answered 202 with no body or a 4xx; the session is open either way
    return sid


def rpc(sid, rid, method, params=None, opener=urllib.request.urlopen):
    body = {"jsonrpc": "2.0", "id": rid, "method": method}
    if params is not None:
        body["params"] = params
    _, msgs = post(body, sid, opener=opener)
    for m in msgs:
        if m.get("id") == rid:
            return m
    raise RuntimeError("no response with id %s for %s" % (rid, method))


def call_tool(sid, name, args, opener=urllib.request.urlopen):
    return rpc(sid, 2, "tools/call", {"name": name, "arguments": args}, opener=opener)


def read_resource(sid, uri, opener=urllib.request.urlopen):
    m = rpc(sid, 5, "resources/read", {"uri": uri}, opener=opener)
    if "error" in m:
        raise RuntimeError(json.dumps(m["error"]))
    parts = m["result"]["contents"]
    return "\n".join(p.get("text", "") for p in parts)


def instances(sid, opener=urllib.request.urlopen):
    return json.loads(read_resource(sid, "mcpforunity://instances", opener=opener))


def tool_result(m):
    """(exit code, printable dict) for a tools/call response."""
    if "error" in m:
        return 1, {"ok": False, "error": m["error"]}
    res = m["result"]
    out = res.get("structuredContent") or {"content": res.get("content")}
    return (1 if res.get("isError") else 0), out


def main(argv, opener=urllib.request.urlopen, out=None, sleep=time.sleep, now=time.time):
    out = out or sys.stdout
    if len(argv) < 2 or argv[1] in ("-h", "--help"):
        print(__doc__, file=out); return 2
    cmd = argv[1]
    try:
        sid = open_session(opener=opener)
    except (urllib.error.URLError, OSError, RuntimeError) as ex:
        print(json.dumps({"ok": False, "error": "no MCP server at %s: %s" % (url(), ex)}), file=out)
        return 2
    if cmd == "ping":
        print(json.dumps({"ok": True, "session": sid}), file=out); return 0
    if cmd == "instances":
        print(json.dumps(instances(sid, opener=opener), ensure_ascii=False), file=out); return 0
    if cmd == "tools":
        m = rpc(sid, 3, "tools/list", opener=opener)
        print(json.dumps([t["name"] for t in m["result"]["tools"]]), file=out); return 0
    if cmd == "resource":
        print(read_resource(sid, argv[2], opener=opener), file=out); return 0
    if cmd == "wait-instance":
        name = argv[2]
        limit = float(argv[3]) if len(argv) > 3 else 120.0
        t0 = now()
        while True:
            found = [i for i in instances(sid, opener=opener).get("instances", []) if str(i.get("name", "")).startswith(name)]
            if found:
                print(json.dumps({"ok": True, "instance": found[0]["id"], "after_s": round(now() - t0, 1)}), file=out); return 0
            if now() - t0 >= limit:
                print(json.dumps({"ok": False, "error": "no Editor named %s* connected after %ss" % (name, int(limit))}), file=out); return 1
            sleep(2)
    if cmd == "call":
        name, args = argv[2], json.loads(argv[3] if len(argv) > 3 else "{}")
    elif cmd == "code":
        name, args = "execute_code", {"action": "execute", "code": argv[2]}
    else:
        print(__doc__, file=out); return 2
    code, res = tool_result(call_tool(sid, name, args, opener=opener))
    print(json.dumps(res, ensure_ascii=False), file=out)
    return code


if __name__ == "__main__":
    # UTF-8 out regardless of the console code page (F-043: cp1252 consoles crash on arrows and dashes)
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
    sys.exit(main(sys.argv))
