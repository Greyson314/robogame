#!/usr/bin/env python3
"""perf_band.py -- a SETTLED idle-frame band from the live factory Editor.

Runs PerfBaselineHarness.<Scene>_Idle_Baseline over the MCP bridge (HTTP,
mcp_http.py) N warm-up times (discarded) then M measured times, reads the
[PERF-BASELINE] rows the harness appends to docs/perf-captures/harness-log.txt,
and prints the band (min..max and spread per metric) so LOOP-STATE § THE BUILD
can quote it (charter D6: every proxy carries its band; ENGINEERING RULE 7:
live-Editor numbers, never compared with the -nographics rig).

    python .claude/scripts/factory/perf_band.py --scene Garage
    python .claude/scripts/factory/perf_band.py --scene Arena --warmups 2 --runs 5

Rules it enforces (LESSONS 12/13, shifts 7 and 8):
  * the rig is shared: before EVERY run it waits (up to --rig-wait s) while a
    Unity.exe with `-runTests` on its command line exists. NOT `-batchmode`:
    the live Editor's AssetImportWorker helpers carry `-batchMode` too.
  * it prints the first run_tests response before trusting its shape
    (job_id lives under result.data).
  * it records the Editor's focus (isApplicationActive) before and after each
    run, because the shift-5 and shift-7 series showed focus moving the numbers
    (CHG-026 also writes `focus=` into the row itself).
Everything goes to --out (default .utmp/factory/perf-band-<scene>-<UTC date>.txt).
"""
import argparse
import datetime as dt
import io
import json
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))

LOG = os.path.join('docs', 'perf-captures', 'harness-log.txt')
ROW = re.compile(r'\[PERF-BASELINE\] scene=(?P<scene>\w+) .*?avg=(?P<avg>[\d.]+)ms median=(?P<median>[\d.]+)ms '
                 r'min=(?P<min>[\d.]+)ms p99=(?P<p99>[\d.]+)ms p99\.9=(?P<p999>[\d.]+)ms fps=[\d.]+ '
                 r'gcTotal=(?P<gc>\d+)B(?: gcPerFrame=[\d.]+B)?(?: focus=(?P<focus>\S+))?')
METRICS = (('avg', 'avg'), ('median', 'median'), ('p99', 'p99'), ('p999', 'p99.9'))


def parse_rows(rows):
    """[PERF-BASELINE] rows -> list of dicts (floats for the metrics, int gc, focus or None). Unparsable rows are skipped."""
    out = []
    for r in rows:
        mm = ROW.search(r)
        if not mm:
            continue
        d = mm.groupdict()
        out.append({'scene': d['scene'], 'avg': float(d['avg']), 'median': float(d['median']), 'min': float(d['min']),
                    'p99': float(d['p99']), 'p999': float(d['p999']), 'gc': int(d['gc']), 'focus': d.get('focus')})
    return out


def band(measured):
    """{metric: (min, max, spread)} over the measured rows, plus 'gc': sorted distinct totals and 'focus': sorted distinct values."""
    if not measured:
        return {}
    b = {}
    for key, _label in METRICS:
        vals = [x[key] for x in measured]
        b[key] = (min(vals), max(vals), max(vals) - min(vals))
    b['gc'] = sorted(set(x['gc'] for x in measured))
    b['focus'] = sorted(set(str(x['focus']) for x in measured if x['focus'] is not None))
    return b


def band_line(scene, measured, warmups, day):
    b = band(measured)
    if not b:
        return 'BAND: no measured rows parsed; nothing to quote'
    parts = ['%s %.2f-%.2f ms (spread %.2f)' % ((label,) + b[key]) for key, label in METRICS]
    focus = (' focus=' + '/'.join(b['focus'])) if b['focus'] else ''
    return 'BAND %s idle, live factory Editor, n=%d after %d warm-ups, %s: %s; gcTotal %s B%s' % (
        scene, len(measured), warmups, day, '; '.join(parts), '/'.join(str(g) for g in b['gc']), focus)


def rig_busy():
    """True while a Unity batch TEST run (-runTests) is alive. Windows only; elsewhere assume idle."""
    if os.name != 'nt':
        return False
    ps = ("Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | "
          "ForEach-Object { $_.CommandLine } | Where-Object { $_ -match '-runTests' } | Measure-Object | "
          "Select-Object -ExpandProperty Count")
    try:
        out = subprocess.run(['powershell', '-NoProfile', '-Command', ps], capture_output=True, text=True, timeout=30).stdout.strip()
        return out.isdigit() and int(out) > 0
    except Exception:
        return False


def wait_for_rig(limit_s, say):
    t0 = time.time()
    waited = False
    while rig_busy():
        if not waited:
            say('rig busy (a -runTests Unity.exe is alive): waiting')
            waited = True
        if time.time() - t0 > limit_s:
            say('rig still busy after %d s: giving up on this run' % limit_s)
            return False
        time.sleep(10)
    if waited:
        say('rig free after %.0f s' % (time.time() - t0))
    return True


def main():
    sys.path.insert(0, HERE)
    import mcp_http as m

    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--scene', default='Arena', choices=['Arena', 'Garage'])
    ap.add_argument('--warmups', type=int, default=2)
    ap.add_argument('--runs', type=int, default=5)
    ap.add_argument('--rig-wait', type=int, default=600, help='seconds to wait for a -runTests Unity.exe to finish before each run')
    ap.add_argument('--out', default=None)
    a = ap.parse_args()

    today = dt.datetime.now(dt.timezone.utc).strftime('%Y-%m-%d')
    out_path = a.out or os.path.join('.utmp', 'factory', 'perf-band-%s-%s.txt' % (a.scene.lower(), today))
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    out = io.open(out_path, 'a', encoding='utf-8')

    def say(s):
        line = '%s %s' % (dt.datetime.now(dt.timezone.utc).strftime('%H:%M:%SZ'), s)
        print(line, flush=True)
        out.write(line + '\n')
        out.flush()

    sid = m.open_session()

    def call(name, args):
        code, res = m.tool_result(m.call_tool(sid, name, args))
        res = res.get('result', res) if isinstance(res, dict) else res
        return code, res

    def focus():
        c, r = call('execute_code', {'action': 'execute',
                                     'code': 'return "active=" + UnityEditorInternal.InternalEditorUtility.isApplicationActive;'})
        return (r.get('data') or {}).get('result', r) if isinstance(r, dict) else r

    test = 'Robogame.Tests.PlayMode.Perf.PerfBaselineHarness.%s_Idle_Baseline' % a.scene
    before = sum(1 for _ in io.open(LOG, encoding='utf-8')) if os.path.exists(LOG) else 0
    say('perf_band %s warmups=%d runs=%d test=%s' % (a.scene, a.warmups, a.runs, test))
    say('focus before: %s' % focus())
    t0 = time.time()
    total = a.warmups + a.runs
    for i in range(total):
        label = 'warmup %d/%d' % (i + 1, a.warmups) if i < a.warmups else 'run %d/%d' % (i - a.warmups + 1, a.runs)
        if not wait_for_rig(a.rig_wait, say):
            break
        job = None
        for attempt in range(20):
            c, r = call('run_tests', {'mode': 'PlayMode', 'test_names': [test]})
            if i == 0 and attempt == 0:
                say('first run_tests response: %s' % json.dumps(r)[:300])  # LESSONS 13: look before looping
            job = (r.get('data') or {}).get('job_id') if isinstance(r, dict) else None
            if job:
                break
            time.sleep(6)
        if not job:
            say('%s: no job after 20 attempts: %s' % (label, json.dumps(r)[:300]))
            break
        status, d = None, {}
        for _ in range(12):
            c, jr = call('get_test_job', {'job_id': job, 'wait_timeout': 60})
            d = (jr.get('data') or {}) if isinstance(jr, dict) else {}
            status = d.get('status') or d.get('state') or (jr.get('message') if isinstance(jr, dict) else None)
            if any(k in str(status).lower() for k in ('complete', 'finished', 'succeeded', 'failed')):
                break
        summary = json.dumps(d.get('summary') or d.get('result') or '')[:120]
        say('%s job=%s status=%s summary=%s focus=%s t=%.0fs' % (label, job, str(status)[:60], summary, focus(), time.time() - t0))
        time.sleep(3)

    rows = [l.rstrip() for l in io.open(LOG, encoding='utf-8')][before:] if os.path.exists(LOG) else []
    say('new harness rows: %d (first %d are warm-ups, discarded from the band)' % (len(rows), a.warmups))
    for l in rows:
        say('  ' + l)
    say(band_line(a.scene, parse_rows(rows[a.warmups:]), a.warmups, today))
    say('written to %s' % out_path)
    out.close()


if __name__ == '__main__':
    main()
