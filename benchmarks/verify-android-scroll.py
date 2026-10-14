"""Qualify two physical Android scroll runs using observed row displacement."""
import argparse
import json
import pathlib
import re

p = argparse.ArgumentParser()
p.add_argument('dotnative', type=pathlib.Path)
p.add_argument('maui', type=pathlib.Path)
a = p.parse_args()

def require(condition, detail):
    if not condition: raise RuntimeError(f"Scroll parity failed: {detail}")

def read(directory, framework):
    meta = json.loads((directory / 'metadata.json').read_text())
    require(meta['framework'] == framework and meta['passed'] and not meta['emulated'], 'expected successful physical run')
    require(meta['contract'] == 'dotnative-maui-v4', 'contract')
    evidence = [json.loads(line) for line in re.findall(r'scrollEvidence=(.*)', (directory / 'instrumentation.txt').read_text())]
    require(len(evidence) == meta['iterations'], (len(evidence), meta['iterations']))
    for leg in evidence:
        require(len(leg['movements']) == 10, 'ten measured drags')
        require(len(leg['durationMs']) == 10 and all(2400 <= x <= 2440 for x in leg['durationMs']), 'input cadence')
        require(abs(leg['netPixels']) <= leg['gesturePixels'] * .1, leg)
    return meta, evidence

dn, de = read(a.dotnative, 'dotnative')
ma, me = read(a.maui, 'maui')
for key in ('device', 'fingerprint', 'selection', 'iterations', 'instrumentation_apk_sha256', 'compilation_mode'):
    require(dn[key] == ma[key], key)
for left, right in zip(de, me):
    for key in ('variable', 'width', 'height', 'gesturePixels', 'targetPixels'):
        require(left[key] == right[key], (key, left, right))
    require(left['initialRows'].keys() == right['initialRows'].keys(), (left, right))
    require(all(abs(left['initialRows'][key] - right['initialRows'][key]) <= 2 for key in left['initialRows']), (left, right))
    require(all(abs(x-y) <= 20 for x,y in zip(left['durationMs'], right['durationMs'])), 'paired input duration')
    require(all(abs(x-y) <= 2 for x,y in zip(left['movements'], right['movements'])), (left, right))
print(json.dumps({'matched': True, 'iterations': len(de), 'selection': dn['selection'],
                  'dotnative_absolute_pixels': [sum(map(abs, x['movements'])) for x in de],
                  'maui_absolute_pixels': [sum(map(abs, x['movements'])) for x in me]}, indent=2))
