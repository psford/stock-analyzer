#!/usr/bin/env python3
"""
Simple concurrent load test for Stock Analyzer API.
Tests for thread contention and response time degradation under load.
"""

import argparse
import asyncio
import aiohttp
import time
import statistics
from collections import defaultdict


async def fetch(session: aiohttp.ClientSession, url: str, results: list):
    """Make a single request and record timing."""
    start = time.perf_counter()
    try:
        async with session.get(url, timeout=aiohttp.ClientTimeout(total=30)) as response:
            await response.read()
            elapsed = (time.perf_counter() - start) * 1000  # ms
            results.append({
                "url": url,
                "status": response.status,
                "time_ms": elapsed,
                "error": None
            })
    except Exception as e:
        elapsed = (time.perf_counter() - start) * 1000
        results.append({
            "url": url,
            "status": 0,
            "time_ms": elapsed,
            "error": str(e)
        })


async def run_load_test(base_url: str, endpoints: list, concurrency: int, requests_per_endpoint: int):
    """Run concurrent requests against multiple endpoints."""
    results = []

    connector = aiohttp.TCPConnector(limit=concurrency * 2)
    async with aiohttp.ClientSession(connector=connector) as session:
        tasks = []
        for endpoint in endpoints:
            url = f"{base_url}{endpoint}"
            for _ in range(requests_per_endpoint):
                tasks.append(fetch(session, url, results))

        print(f"Starting {len(tasks)} requests ({concurrency} concurrent)...")
        start = time.perf_counter()

        # Run with semaphore to control concurrency
        semaphore = asyncio.Semaphore(concurrency)

        async def bounded_fetch(task):
            async with semaphore:
                return await task

        await asyncio.gather(*[bounded_fetch(t) for t in tasks])

        total_time = time.perf_counter() - start

    return results, total_time


def analyze_results(results: list, total_time: float):
    """Analyze and print results."""
    by_endpoint = defaultdict(list)
    for r in results:
        by_endpoint[r["url"]].append(r)

    print("\n" + "=" * 70)
    print("LOAD TEST RESULTS")
    print("=" * 70)

    total_requests = len(results)
    total_errors = sum(1 for r in results if r["error"])
    all_times = [r["time_ms"] for r in results if not r["error"]]

    print(f"\nOverall:")
    print(f"  Total requests:    {total_requests}")
    print(f"  Total errors:      {total_errors} ({total_errors/total_requests*100:.1f}%)")
    print(f"  Total time:        {total_time:.2f}s")
    print(f"  Requests/sec:      {total_requests/total_time:.1f}")

    if all_times:
        print(f"\nResponse Times (successful requests):")
        print(f"  Min:               {min(all_times):.1f}ms")
        print(f"  Max:               {max(all_times):.1f}ms")
        print(f"  Mean:              {statistics.mean(all_times):.1f}ms")
        print(f"  Median:            {statistics.median(all_times):.1f}ms")
        print(f"  Std Dev:           {statistics.stdev(all_times) if len(all_times) > 1 else 0:.1f}ms")
        print(f"  P95:               {sorted(all_times)[int(len(all_times)*0.95)]:.1f}ms")
        print(f"  P99:               {sorted(all_times)[int(len(all_times)*0.99)]:.1f}ms")

    print(f"\nPer Endpoint:")
    for url, endpoint_results in sorted(by_endpoint.items()):
        times = [r["time_ms"] for r in endpoint_results if not r["error"]]
        errors = sum(1 for r in endpoint_results if r["error"])
        endpoint_name = url.split("/")[-1] or url.split("/")[-2]

        print(f"\n  {endpoint_name}:")
        print(f"    Requests: {len(endpoint_results)}, Errors: {errors}")
        if times:
            print(f"    Mean: {statistics.mean(times):.1f}ms, P95: {sorted(times)[int(len(times)*0.95)]:.1f}ms")

    # Check for contention indicators
    print("\n" + "-" * 70)
    print("CONTENTION ANALYSIS")
    print("-" * 70)

    if all_times:
        p50 = statistics.median(all_times)
        p99 = sorted(all_times)[int(len(all_times)*0.99)]
        ratio = p99 / p50 if p50 > 0 else 0

        if ratio > 10:
            print(f"  WARNING: High P99/P50 ratio ({ratio:.1f}x) - possible contention")
        elif ratio > 5:
            print(f"  CAUTION: Elevated P99/P50 ratio ({ratio:.1f}x) - monitor under load")
        else:
            print(f"  OK: P99/P50 ratio ({ratio:.1f}x) looks healthy")

        if max(all_times) > 5000:
            print(f"  WARNING: Max response time {max(all_times):.0f}ms exceeds 5s")

        if total_errors > total_requests * 0.01:
            print(f"  WARNING: Error rate {total_errors/total_requests*100:.1f}% exceeds 1%")

    print()


def main():
    parser = argparse.ArgumentParser(description="Load test Stock Analyzer API")
    parser.add_argument("--url", default="http://localhost:5000", help="Base URL")
    parser.add_argument("-c", "--concurrency", type=int, default=50, help="Concurrent requests")
    parser.add_argument("-n", "--requests", type=int, default=20, help="Requests per endpoint")
    parser.add_argument("--quick", action="store_true", help="Quick test (10 requests, 20 concurrency)")
    parser.add_argument("--heavy", action="store_true", help="Heavy test (100 requests, 100 concurrency)")
    parser.add_argument("--images-only", action="store_true", help="Test only image endpoints")
    parser.add_argument("--db-only", action="store_true", help="Test only database endpoints")
    args = parser.parse_args()

    if args.quick:
        args.concurrency = 20
        args.requests = 10
    elif args.heavy:
        args.concurrency = 100
        args.requests = 100

    # Endpoint sets
    if args.images_only:
        endpoints = [
            "/api/images/cat",
            "/api/images/dog",
            "/api/images/status",
        ]
    elif args.db_only:
        endpoints = [
            "/api/images/cat",
            "/api/images/dog",
            "/api/images/status",
            "/api/search?q=app",
            "/api/search?q=micro",
            "/api/search?q=goo",
        ]
    else:
        # Default: focus on image cache and database-heavy operations
        endpoints = [
            "/api/images/cat",
            "/api/images/dog",
            "/api/images/status",
            "/api/search?q=app",
            "/health",
        ]

    print(f"Load Test: {args.url}")
    print(f"Concurrency: {args.concurrency}, Requests per endpoint: {args.requests}")
    print(f"Endpoints: {len(endpoints)}")

    results, total_time = asyncio.run(
        run_load_test(args.url, endpoints, args.concurrency, args.requests)
    )

    analyze_results(results, total_time)


if __name__ == "__main__":
    main()
