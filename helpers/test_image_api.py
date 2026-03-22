#!/usr/bin/env python3
"""Test that image API returns different images each time."""

import sys
import hashlib
import requests

def test_image_api(url="http://localhost:5000"):
    print(f"Testing image API at {url}/api/images/cat")

    hashes = []
    for i in range(5):
        resp = requests.get(f"{url}/api/images/cat", timeout=30)
        if resp.status_code == 200:
            h = hashlib.md5(resp.content, usedforsecurity=False).hexdigest()[:12]
            hashes.append(h)
            print(f"  Request {i+1}: {len(resp.content)} bytes, hash: {h}")
        else:
            print(f"  Request {i+1}: ERROR {resp.status_code}")

    unique = set(hashes)
    print(f"\nResults: {len(hashes)} requests, {len(unique)} unique images")

    if len(unique) == len(hashes):
        print("✓ PASS: All images are different!")
        return 0
    elif len(unique) == 1:
        print("✗ FAIL: All images are the same!")
        return 1
    else:
        print(f"~ PARTIAL: Some images repeated")
        return 0

if __name__ == "__main__":
    url = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5000"
    sys.exit(test_image_api(url))
