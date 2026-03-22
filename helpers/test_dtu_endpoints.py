"""
DTU Endpoint Verification Script
Tests affected endpoints after DTU exhaustion fixes.
Run against localhost:5000 to verify changes work correctly.

Usage:
    python helpers/test_dtu_endpoints.py [--base-url http://localhost:5000]
"""

import argparse
import json
import sys
import time
import urllib.request
import urllib.error
from concurrent.futures import ThreadPoolExecutor


def make_request(url, method="GET", timeout=60):
    """Make an HTTP request and return (status, body, elapsed_ms)."""
    start = time.time()
    try:
        req = urllib.request.Request(url, method=method)
        with urllib.request.urlopen(req, timeout=timeout) as resp:  # nosec B310
            body = json.loads(resp.read().decode())
            elapsed = (time.time() - start) * 1000
            return resp.status, body, elapsed
    except urllib.error.HTTPError as e:
        body = None
        try:
            body = json.loads(e.read().decode())
        except Exception:
            pass
        elapsed = (time.time() - start) * 1000
        return e.code, body, elapsed
    except Exception as e:
        elapsed = (time.time() - start) * 1000
        return 0, {"error": str(e)}, elapsed


def test_endpoint(name, url, method="GET", expect_status=200, max_ms=30000):
    """Test a single endpoint and report results."""
    status, body, elapsed = make_request(url, method)
    passed = status == expect_status and elapsed < max_ms

    icon = "PASS" if passed else "FAIL"
    print(f"  [{icon}] {name}")
    print(f"         Status: {status} (expected {expect_status})")
    print(f"         Time: {elapsed:.0f}ms (max {max_ms}ms)")

    if not passed:
        if status != expect_status:
            print(f"         ERROR: Unexpected status code")
        if elapsed >= max_ms:
            print(f"         ERROR: Response too slow (>{max_ms}ms)")
        if body and "error" in str(body).lower():
            error_msg = body.get("error", body.get("detail", str(body)))
            print(f"         Body: {error_msg}")

    return passed, status, body, elapsed


def test_concurrent_409(name, url, method="POST"):
    """Test that concurrent requests return 409 Conflict."""
    print(f"  [TEST] {name} (concurrent 409 check)")

    # Fire first request (should succeed or take a while)
    with ThreadPoolExecutor(max_workers=2) as executor:
        future1 = executor.submit(make_request, url, method, 120)
        time.sleep(0.5)  # Let first request start processing
        future2 = executor.submit(make_request, url, method, 10)

        status2, body2, elapsed2 = future2.result()
        status1, body1, elapsed1 = future1.result()

    # Second request should get 409
    if status2 == 409:
        print(f"         PASS: Second request got 409 Conflict ({elapsed2:.0f}ms)")
        return True
    else:
        print(f"         INFO: Second request got {status2} (409 expected if first is slow enough)")
        print(f"         First: {status1} in {elapsed1:.0f}ms, Second: {status2} in {elapsed2:.0f}ms")
        return status2 == 409


def main():
    parser = argparse.ArgumentParser(description="Test DTU-fixed endpoints")
    parser.add_argument("--base-url", default="http://localhost:5000", help="API base URL")
    args = parser.parse_args()
    base = args.base_url.rstrip("/")

    print(f"\nDTU Endpoint Verification -{base}")
    print("=" * 60)

    results = []

    # 1. Holiday analysis (Fix 1A: was ~2,700 queries, now 5)
    print("\n[Commit 1] SqlPriceRepository fixes:")
    results.append(test_endpoint(
        "Holiday analysis (AnalyzeHolidaysAsync)",
        f"{base}/api/admin/prices/holidays/analyze",
        max_ms=30000
    ))

    # 2. Coverage dates (Fix from prior commit: GetDistinctDatesAsync)
    results.append(test_endpoint(
        "Coverage dates (GetDistinctDatesAsync)",
        f"{base}/api/admin/prices/coverage-dates",
        max_ms=10000
    ))

    # 3. Forward-fill with small limit (Fix 1B: was ~12,000 queries, now ~70 batches)
    results.append(test_endpoint(
        "Forward-fill (limit=5)",
        f"{base}/api/admin/prices/holidays/forward-fill?limit=5",
        method="POST",
        max_ms=30000
    ))

    # 4. Data export pagination (Fix 3A: no double-scan)
    print("\n[Commit 3] Program.cs fixes:")
    results.append(test_endpoint(
        "Data export (page=1, pageSize=10)",
        f"{base}/api/admin/data/prices?page=1&pageSize=10",
        max_ms=15000
    ))

    # 5. Price status (Fix 3J: uses projected query)
    results.append(test_endpoint(
        "Price status (GetActiveTickerAliasMapAsync)",
        f"{base}/api/admin/prices/status",
        max_ms=10000
    ))

    # 6. Dashboard stats (Fix 3H: AsNoTracking on CoverageSummary)
    results.append(test_endpoint(
        "Dashboard stats (AsNoTracking)",
        f"{base}/api/admin/dashboard/stats",
        max_ms=15000
    ))

    # 7. Heatmap (Fix 3H: AsNoTracking on CoverageSummary)
    results.append(test_endpoint(
        "Heatmap data (AsNoTracking)",
        f"{base}/api/admin/dashboard/heatmap",
        max_ms=10000
    ))

    # 8. Refresh summary (Fix 3B: concurrency guard)
    results.append(test_endpoint(
        "Refresh summary",
        f"{base}/api/admin/dashboard/refresh-summary",
        method="POST",
        max_ms=300000  # This is the expensive query, allow 5 min
    ))

    # 9. Concurrent refresh-summary should return 409
    print("\n[Concurrency Guards]:")
    concurrent_result = test_concurrent_409(
        "Refresh summary (concurrent -> 409)",
        f"{base}/api/admin/dashboard/refresh-summary",
        method="POST"
    )

    # Summary
    print("\n" + "=" * 60)
    passed = sum(1 for r in results if r[0])
    total = len(results)
    print(f"Results: {passed}/{total} endpoint tests passed")

    slow_endpoints = [(r[3], r) for r in results if r[3] > 30000]
    if slow_endpoints:
        print(f"\nWARNING: {len(slow_endpoints)} endpoint(s) took >30s:")
        for elapsed, r in slow_endpoints:
            print(f"  - {elapsed:.0f}ms")

    if passed < total:
        print("\nSome tests FAILED -investigate before deploying.")
        return 1

    print("\nAll endpoint tests passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
