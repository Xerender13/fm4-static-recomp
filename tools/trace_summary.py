"""trace_summary.py <runtime.log> [seconds]: rates and spacing of the SDK's critical path events
(guest vblank, guest swap, consumed swap, present) over the log's last seconds, and how presents
map to guest frames. Run the game with --perf_critical_path_trace=true; the log rotates fast."""
import json, re, sys, collections

path = sys.argv[1]
tail_s = float(sys.argv[2]) if len(sys.argv) > 2 else 20.0
events = collections.defaultdict(list)
pat = re.compile(r'CRITICAL_PATH (\{.*\})')
with open(path, encoding='utf-8', errors='replace') as f:
    for line in f:
        m = pat.search(line)
        if m:
            e = json.loads(m.group(1))
            events[e['event']].append(e)

end = max(e[-1]['time_ns'] for e in events.values())
start = end - int(tail_s * 1e9)
print(f'window: last {tail_s:.0f} s')
for name, es in sorted(events.items()):
    es = [e for e in es if e['time_ns'] >= start]
    if len(es) < 2:
        print(f'{name}: {len(es)} events')
        continue
    d = sorted((b['time_ns'] - a['time_ns']) / 1e6 for a, b in zip(es, es[1:]))
    q = lambda p: d[min(len(d) - 1, int(p * len(d)))]
    threads = len({e['thread'] for e in es})
    span = (es[-1]['time_ns'] - es[0]['time_ns']) / 1e9
    print(f'{name}: {(len(es) - 1) / span:.1f}/s over {span:.1f}s threads={threads} '
          f'delta ms p1={q(.01):.2f} p50={q(.5):.2f} p99={q(.99):.2f} max={d[-1]:.2f}')

gs, cs = events.get('guest_swap', []), events.get('consumed_swap', [])
if gs and cs:
    # The log rotates, so pair each consumed swap with the oldest unconsumed
    # guest swap of the same frontbuffer.
    merged = sorted([(e['time_ns'], 0, e['value0']) for e in gs] +
                    [(e['time_ns'], 1, e['value0']) for e in cs])
    pending = collections.defaultdict(collections.deque)
    lat = []
    for t, kind, fb in merged:
        if kind == 0:
            pending[fb].append(t)
        elif pending[fb]:
            g = pending[fb].popleft()
            if t >= start:
                lat.append((t - g) / 1e6)
    lat.sort()
    if lat:
        print(f'guest_swap -> consumed_swap ms p50={lat[len(lat) // 2]:.2f} '
              f'p99={lat[int(.99 * len(lat))]:.2f} max={lat[-1]:.2f}')
pt = {e['thread'] for e in events.get('present', []) if e['time_ns'] >= start}
ct = {e['thread'] for e in cs if e['time_ns'] >= start}
print('present on swap thread:', pt == ct, f'({len(pt)} present thread(s))')

pres = [e for e in events.get('present', []) if e['time_ns'] >= start]
if pres:
    seqs = [e['value0'] for e in pres]
    dup = sum(1 for a, b in zip(seqs, seqs[1:]) if a == b)
    skipped = sum(b - a - 1 for a, b in zip(seqs, seqs[1:]) if b > a + 1)
    print(f'presents={len(seqs)} duplicate={dup} refreshes_never_presented={skipped} '
          f'refreshes={seqs[-1] - seqs[0]}')
    runs = collections.Counter()
    n = 1
    for a, b in zip(seqs, seqs[1:]):
        if a == b:
            n += 1
        else:
            runs[n] += 1
            n = 1
    print('presents per refresh histogram:', dict(sorted(runs.items())))
