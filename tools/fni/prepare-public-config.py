#!/usr/bin/env python3
"""Export an explicit allowlist of public mobile settings; never copy .env into a bundle."""
import argparse
import json
from pathlib import Path

ALLOWED = frozenset('''FNI_API_URL FNI_CHAT_URL FNI_PLACES_KEY FNI_REGION FNI_POOL_ID FNI_CLIENT_ID
FNI_FIREBASE_PROJECT_ID FNI_FIREBASE_SENDER_ID FNI_FIREBASE_STORAGE_BUCKET FNI_IOS_BUNDLE_ID
FNI_IOS_FIREBASE_API_KEY FNI_IOS_FIREBASE_APP_ID FNI_ANDROID_FIREBASE_API_KEY FNI_ANDROID_FIREBASE_APP_ID
FNI_PROFILE_IDENTITY_POOL_ID FNI_PROFILE_STORAGE_REGION FNI_PROFILE_IMAGE_BUCKET'''.split())


def export(source: Path, destination: Path):
    values = {}
    for raw in source.read_text().splitlines():
        line = raw.strip()
        if not line or line.startswith('#'):
            continue
        key, sep, value = line.removeprefix('export ').partition('=')
        key, value = key.strip(), value.strip()
        if sep and key in ALLOWED:
            if key in values:
                raise ValueError('Duplicate public configuration key: ' + key)
            if value.startswith(('"', "'")):
                if not value.endswith(value[0]):
                    raise ValueError('Unclosed quoted public configuration value')
                value = value[1:-1]
            if value:
                values[key] = value
    destination.write_text(json.dumps(values, ensure_ascii=False, indent=2) + '\n')
    print(f'Exported {len(values)} public settings; values are not logged.')
    missing = ALLOWED - values.keys() - {'FNI_CHAT_URL'}
    if missing:
        print('Missing settings: ' + ', '.join(sorted(missing)))

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    export(args.source, args.destination)
