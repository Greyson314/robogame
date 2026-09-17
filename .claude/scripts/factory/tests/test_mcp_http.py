import io, json, sys, unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import mcp_http  # noqa: E402


class FakeResponse:
    def __init__(self, body, session="sess-1"):
        self.headers = {"mcp-session-id": session} if session else {}
        self._body = body.encode("utf-8")
    def read(self): return self._body
    def __enter__(self): return self
    def __exit__(self, *a): return False


def sse(*objs):
    return "".join("event: message\ndata: %s\n\n" % json.dumps(o) for o in objs)


class FakeServer:
    """Records every request; answers by JSON-RPC method. Raises like urlopen when 'down'."""
    def __init__(self, down=False, instances=None):
        self.down, self.requests, self.instances = down, [], instances or []
    def __call__(self, req, timeout=None):
        if self.down:
            import urllib.error
            raise urllib.error.URLError("connection refused")
        body = json.loads(req.data.decode("utf-8"))
        self.requests.append((dict(req.headers), body))
        m, rid = body.get("method"), body.get("id")
        if m == "initialize":
            return FakeResponse(sse({"jsonrpc": "2.0", "id": rid, "result": {"protocolVersion": "2025-03-26", "serverInfo": {"name": "fake"}}}))
        if m == "notifications/initialized":
            return FakeResponse("", session=None)
        if m == "tools/list":
            return FakeResponse(sse({"jsonrpc": "2.0", "id": rid, "result": {"tools": [{"name": "read_console"}, {"name": "set_active_instance"}]}}))
        if m == "tools/call":
            name = body["params"]["name"]
            if name == "boom":
                return FakeResponse(sse({"jsonrpc": "2.0", "id": rid, "error": {"code": -32602, "message": "no such tool"}}))
            if name == "failing":
                return FakeResponse(sse({"jsonrpc": "2.0", "id": rid, "result": {"content": [{"type": "text", "text": "x"}], "isError": True}}))
            # a bare-JSON (non-SSE) framing, as some transports answer
            return FakeResponse(json.dumps({"jsonrpc": "2.0", "id": rid, "result": {"structuredContent": {"success": True, "echo": body["params"]["arguments"]}, "isError": False}}))
        if m == "resources/read":
            payload = {"success": True, "instance_count": len(self.instances), "instances": self.instances}
            return FakeResponse(sse({"jsonrpc": "2.0", "id": rid, "result": {"contents": [{"uri": body["params"]["uri"], "mimeType": "text/plain", "text": json.dumps(payload)}]}}))
        raise AssertionError("unexpected method " + str(m))


def run(argv, server, **kw):
    out = io.StringIO()
    code = mcp_http.main(["mcp_http.py"] + argv, opener=server, out=out, **kw)
    return code, out.getvalue().strip()


class Framing(unittest.TestCase):
    def test_initialize_then_initialized_then_session_header_on_every_later_call(self):
        srv = FakeServer()
        code, text = run(["call", "read_console", '{"action":"get"}'], srv)
        self.assertEqual(code, 0)
        methods = [b.get("method") for _, b in srv.requests]
        self.assertEqual(methods, ["initialize", "notifications/initialized", "tools/call"])
        # every message after initialize carries the session id the server handed back
        for h, b in srv.requests[1:]:
            self.assertEqual(h.get("Mcp-session-id") or h.get("mcp-session-id"), "sess-1", h)
        h0, b0 = srv.requests[0]
        self.assertEqual(b0["params"]["protocolVersion"], mcp_http.PROTOCOL)
        self.assertIn("text/event-stream", h0.get("Accept", ""))
        self.assertEqual(json.loads(text)["echo"], {"action": "get"})

    def test_parses_sse_and_bare_json_framings(self):
        self.assertEqual(mcp_http.parse_body(sse({"a": 1}, {"b": 2})), [{"a": 1}, {"b": 2}])
        self.assertEqual(mcp_http.parse_body('{"a": 1}\n'), [{"a": 1}])
        self.assertEqual(mcp_http.parse_body("event: ping\n\n"), [])

    def test_code_shorthand_targets_execute_code(self):
        srv = FakeServer()
        code, text = run(["code", "return 1;"], srv)
        self.assertEqual(code, 0)
        _, b = srv.requests[-1]
        self.assertEqual(b["params"]["name"], "execute_code")
        self.assertEqual(b["params"]["arguments"], {"action": "execute", "code": "return 1;"})


class ExitCodes(unittest.TestCase):
    def test_no_server_is_exit_2_with_a_json_line(self):
        code, text = run(["ping"], FakeServer(down=True))
        self.assertEqual(code, 2)
        self.assertFalse(json.loads(text)["ok"])

    def test_rpc_error_and_isError_are_exit_1(self):
        self.assertEqual(run(["call", "boom"], FakeServer())[0], 1)
        self.assertEqual(run(["call", "failing"], FakeServer())[0], 1)

    def test_ping_and_tools(self):
        code, text = run(["ping"], FakeServer())
        self.assertEqual((code, json.loads(text)), (0, {"ok": True, "session": "sess-1"}))
        code, text = run(["tools"], FakeServer())
        self.assertEqual((code, json.loads(text)), (0, ["read_console", "set_active_instance"]))


class WaitInstance(unittest.TestCase):
    def test_returns_when_a_matching_editor_is_connected(self):
        srv = FakeServer(instances=[{"id": "robogame-factory@abc", "name": "robogame-factory"}])
        code, text = run(["wait-instance", "robogame-factory", "5"], srv, sleep=lambda s: None)
        self.assertEqual(code, 0)
        self.assertEqual(json.loads(text)["instance"], "robogame-factory@abc")

    def test_does_not_match_greys_checkout_and_times_out(self):
        # Grey's own Editor is named "robogame"; the factory's is "robogame-factory". A prefix match
        # on the FACTORY name must not accept the other clone.
        srv = FakeServer(instances=[{"id": "robogame@def", "name": "robogame"}])
        clock = iter([0, 0, 1, 3, 6, 10, 20, 40])
        code, text = run(["wait-instance", "robogame-factory", "5"], srv, sleep=lambda s: None, now=lambda: next(clock))
        self.assertEqual(code, 1)
        self.assertIn("no Editor named robogame-factory*", json.loads(text)["error"])


if __name__ == "__main__":
    unittest.main()
