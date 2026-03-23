#!/usr/bin/env python3
"""
Cloudflare connectivity and WAF rule diagnostic tool.

Tests endpoint accessibility through Cloudflare and helps diagnose
Bot Fight Mode blocking issues.

Usage:
    python helpers/cloudflare_test.py                    # Basic health check
    python helpers/cloudflare_test.py --verbose          # Detailed output
    python helpers/cloudflare_test.py --check-rules      # Check Cloudflare rules (requires API token)
    python helpers/cloudflare_test.py --ip-check 1.2.3.4 # Check if IP is in GitHub Actions ranges

Requires:
    pip install requests python-dotenv

Environment variables (optional, for --check-rules):
    CLOUDFLARE_API_TOKEN - API token with Zone Read permissions
    CLOUDFLARE_ZONE_ID   - Zone ID for psfordtaurus.com
"""

import argparse
import ipaddress
import json
import os
import sys
from pathlib import Path

try:
    import requests
except ImportError:
    print("Error: requests library required. Install with: pip install requests")
    sys.exit(1)

# GitHub Actions IP ranges (from https://api.github.com/meta)
# These are the ranges we've configured in Cloudflare
GITHUB_ACTIONS_CIDRS = [
    "140.82.112.0/20",
    "143.55.64.0/20",
    "185.199.108.0/22",
    "192.30.252.0/22",
]

# Additional GitHub Actions ranges that may be used
GITHUB_ACTIONS_EXTENDED_CIDRS = [
    "4.148.0.0/16",      # Azure-hosted runners
    "4.175.0.0/16",      # Azure-hosted runners
    "4.148.0.0/15",      # Azure-hosted runners (larger block)
    "20.0.0.0/8",        # Azure general (very broad)
]

DEFAULT_URL = "https://psfordtaurus.com/health/live"
AZURE_DIRECT_URL = "https://app-stockanalyzer-prod.azurewebsites.net/health/live"


def get_public_ip():
    """Get the current machine's public IP address."""
    services = [
        "https://api.ipify.org",
        "https://ifconfig.me/ip",
        "https://icanhazip.com",
    ]
    for service in services:
        try:
            response = requests.get(service, timeout=5)
            if response.status_code == 200:
                return response.text.strip()
        except Exception:
            continue
    return None


def ip_in_cidr(ip_str: str, cidr_list: list[str]) -> tuple[bool, str | None]:
    """Check if an IP address is within any of the given CIDR ranges."""
    try:
        ip = ipaddress.ip_address(ip_str)
        for cidr in cidr_list:
            network = ipaddress.ip_network(cidr, strict=False)
            if ip in network:
                return True, cidr
        return False, None
    except ValueError as e:
        print(f"Invalid IP address: {e}")
        return False, None


def test_endpoint(url: str, verbose: bool = False) -> dict:
    """Test an endpoint and return detailed results."""
    result = {
        "url": url,
        "success": False,
        "status_code": None,
        "response_time_ms": None,
        "headers": {},
        "error": None,
    }

    try:
        response = requests.get(url, timeout=30, allow_redirects=True)
        result["status_code"] = response.status_code
        result["response_time_ms"] = int(response.elapsed.total_seconds() * 1000)
        result["success"] = response.status_code == 200

        if verbose:
            # Capture interesting headers
            interesting_headers = [
                "cf-ray", "cf-cache-status", "server", "content-type",
                "x-frame-options", "content-security-policy"
            ]
            result["headers"] = {
                k: v for k, v in response.headers.items()
                if k.lower() in interesting_headers
            }

    except requests.exceptions.Timeout:
        result["error"] = "Request timed out after 30 seconds"
    except requests.exceptions.ConnectionError as e:
        result["error"] = f"Connection error: {e}"
    except Exception as e:
        result["error"] = f"Unexpected error: {e}"

    return result


def check_cloudflare_rules(zone_id: str, api_token: str, verbose: bool = False):
    """Check Cloudflare custom rules via API."""
    headers = {
        "Authorization": f"Bearer {api_token}",
        "Content-Type": "application/json",
    }

    # Get custom rules (rulesets)
    url = f"https://api.cloudflare.com/client/v4/zones/{zone_id}/rulesets"

    try:
        response = requests.get(url, headers=headers, timeout=10)
        if response.status_code == 200:
            data = response.json()
            print("\n=== Cloudflare Rulesets ===")
            for ruleset in data.get("result", []):
                print(f"  - {ruleset.get('name', 'unnamed')} (phase: {ruleset.get('phase')})")
                if verbose and ruleset.get("id"):
                    # Get ruleset details
                    detail_url = f"https://api.cloudflare.com/client/v4/zones/{zone_id}/rulesets/{ruleset['id']}"
                    detail_resp = requests.get(detail_url, headers=headers, timeout=10)
                    if detail_resp.status_code == 200:
                        detail_data = detail_resp.json()
                        for rule in detail_data.get("result", {}).get("rules", []):
                            print(f"      Rule: {rule.get('description', 'no description')}")
                            print(f"        Expression: {rule.get('expression', 'N/A')}")
                            print(f"        Action: {rule.get('action', 'N/A')}")
        else:
            print(f"Failed to fetch rulesets: {response.status_code}")
            if verbose:
                print(f"Response: {response.text}")
    except Exception as e:
        print(f"Error checking Cloudflare rules: {e}")


def fetch_github_actions_ips():
    """Fetch current GitHub Actions IP ranges from GitHub's meta API."""
    try:
        response = requests.get("https://api.github.com/meta", timeout=10)
        if response.status_code == 200:
            data = response.json()
            return data.get("actions", [])
        return None
    except Exception:
        return None


def main():
    parser = argparse.ArgumentParser(
        description="Test Cloudflare connectivity and diagnose WAF issues"
    )
    parser.add_argument(
        "--verbose", "-v",
        action="store_true",
        help="Show detailed output"
    )
    parser.add_argument(
        "--check-rules",
        action="store_true",
        help="Check Cloudflare WAF rules (requires API token in .env)"
    )
    parser.add_argument(
        "--ip-check",
        type=str,
        help="Check if a specific IP is in GitHub Actions ranges"
    )
    parser.add_argument(
        "--fetch-github-ips",
        action="store_true",
        help="Fetch and display current GitHub Actions IP ranges"
    )
    parser.add_argument(
        "--url",
        type=str,
        default=DEFAULT_URL,
        help=f"URL to test (default: {DEFAULT_URL})"
    )

    args = parser.parse_args()

    # Load .env if present
    env_path = Path(__file__).parent.parent / ".env"
    if env_path.exists():
        try:
            from dotenv import load_dotenv
            load_dotenv(env_path)
        except ImportError:
            pass  # dotenv not installed, will use environment directly

    print("=" * 60)
    print("Cloudflare Connectivity Diagnostic Tool")
    print("=" * 60)

    # Get and check current IP
    print("\n=== Your Public IP ===")
    my_ip = get_public_ip()
    if my_ip:
        print(f"  IP Address: {my_ip}")
        in_gh_range, matched_cidr = ip_in_cidr(my_ip, GITHUB_ACTIONS_CIDRS)
        if in_gh_range:
            print(f"  Status: IN configured GitHub Actions range ({matched_cidr})")
        else:
            in_extended, ext_cidr = ip_in_cidr(my_ip, GITHUB_ACTIONS_EXTENDED_CIDRS)
            if in_extended:
                print(f"  Status: IN extended GitHub/Azure range ({ext_cidr})")
                print("  Note: This range may not be in your Cloudflare rule!")
            else:
                print("  Status: NOT in any known GitHub Actions range")
    else:
        print("  Could not determine public IP")

    # Check specific IP if requested
    if args.ip_check:
        print(f"\n=== Checking IP: {args.ip_check} ===")
        in_gh, cidr = ip_in_cidr(args.ip_check, GITHUB_ACTIONS_CIDRS)
        if in_gh:
            print(f"  IN configured range: {cidr}")
        else:
            in_ext, ext_cidr = ip_in_cidr(args.ip_check, GITHUB_ACTIONS_EXTENDED_CIDRS)
            if in_ext:
                print(f"  IN extended range: {ext_cidr} (may need to add to Cloudflare)")
            else:
                print("  NOT in any known GitHub Actions range")

    # Fetch GitHub IPs if requested
    if args.fetch_github_ips:
        print("\n=== GitHub Actions IP Ranges (from api.github.com/meta) ===")
        gh_ips = fetch_github_actions_ips()
        if gh_ips:
            for cidr in sorted(gh_ips):
                in_our_list = cidr in GITHUB_ACTIONS_CIDRS
                marker = "[CONFIGURED]" if in_our_list else "[NOT CONFIGURED]"
                print(f"  {cidr} {marker}")
        else:
            print("  Could not fetch GitHub Actions IPs")

    # Test endpoints
    print(f"\n=== Testing: {args.url} ===")
    result = test_endpoint(args.url, args.verbose)

    if result["success"]:
        print(f"  Status: OK ({result['status_code']})")
        print(f"  Response time: {result['response_time_ms']}ms")
    else:
        print(f"  Status: FAILED")
        if result["status_code"]:
            print(f"  HTTP Code: {result['status_code']}")
            if result["status_code"] == 403:
                print("  Likely cause: Cloudflare Bot Fight Mode blocking request")
        if result["error"]:
            print(f"  Error: {result['error']}")

    if args.verbose and result["headers"]:
        print("  Headers:")
        for k, v in result["headers"].items():
            print(f"    {k}: {v}")

    # Test direct Azure endpoint (bypasses Cloudflare)
    print(f"\n=== Testing Direct Azure URL (bypasses Cloudflare) ===")
    print(f"  URL: {AZURE_DIRECT_URL}")
    azure_result = test_endpoint(AZURE_DIRECT_URL, args.verbose)

    if azure_result["success"]:
        print(f"  Status: OK ({azure_result['status_code']})")
        print(f"  Response time: {azure_result['response_time_ms']}ms")
        if not result["success"]:
            print("\n  Diagnosis: App is healthy. Issue is with Cloudflare, not the app.")
    else:
        print(f"  Status: FAILED")
        if azure_result["status_code"]:
            print(f"  HTTP Code: {azure_result['status_code']}")
        if azure_result["error"]:
            print(f"  Error: {azure_result['error']}")
        if not result["success"]:
            print("\n  Diagnosis: Both endpoints failing - may be an app issue.")

    # Check Cloudflare rules if requested
    if args.check_rules:
        zone_id = os.environ.get("CLOUDFLARE_ZONE_ID")
        api_token = os.environ.get("CLOUDFLARE_API_TOKEN")

        if zone_id and api_token:
            check_cloudflare_rules(zone_id, api_token, args.verbose)
        else:
            print("\n=== Cloudflare Rules Check ===")
            print("  Skipped: CLOUDFLARE_ZONE_ID and/or CLOUDFLARE_API_TOKEN not set")

    print("\n" + "=" * 60)

    # Return appropriate exit code
    sys.exit(0 if result["success"] else 1)


if __name__ == "__main__":
    main()
