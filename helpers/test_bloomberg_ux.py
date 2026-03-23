#!/usr/bin/env python3
"""
Bloomberg Terminal UX Test Suite

Tests the keyboard-first, auto-complete-on-Tab/Enter workflow:
1. No Analyze button exists
2. Type ticker -> dropdown appears with first item highlighted
3. Tab away -> auto-completes top result -> auto-analyzes
4. Enter -> same behavior (auto-complete + analyze)
5. Comparison field: same Tab/Enter behavior
6. Blur (click away) -> auto-completes + analyzes if value changed

Requires: API running on localhost:5000
"""

import sys
import time
from playwright.sync_api import sync_playwright, expect

BASE_URL = "http://localhost:5000"
RESULTS = []


def log_result(name, passed, detail=""):
    status = "PASS" if passed else "FAIL"
    RESULTS.append((name, passed, detail))
    print(f"  [{status}] {name}" + (f" - {detail}" if detail else ""))


def run_tests():
    print("Bloomberg Terminal UX Test Suite")
    print("=" * 60)

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context(viewport={"width": 1920, "height": 1080})
        page = context.new_page()

        # Collect JS errors
        js_errors = []
        page.on("pageerror", lambda err: js_errors.append(str(err)))

        print("\nLoading page...")
        page.goto(BASE_URL, wait_until="networkidle", timeout=30000)
        # Wait for symbol data to load (needed for client-side search)
        page.wait_for_timeout(2000)

        # ─── Test 1: No Analyze button ───
        print("\n--- Test 1: No Analyze button ---")
        analyze_btn = page.query_selector("#search-btn, #analyze-btn, button:has-text('Analyze')")
        log_result("No Analyze button on page", analyze_btn is None,
                   "Found analyze button!" if analyze_btn else "Correctly removed")

        # ─── Test 2: Clear button exists ───
        print("\n--- Test 2: Clear button exists ---")
        clear_btn = page.query_selector("#clear-btn")
        log_result("Clear button exists", clear_btn is not None)

        # ─── Test 3: Type in ticker -> dropdown appears ───
        print("\n--- Test 3: Dropdown on typing ---")
        ticker_input = page.locator("#ticker-input")
        ticker_input.click()
        ticker_input.fill("")
        ticker_input.type("MSF", delay=100)
        # Wait for debounced search (300ms) + rendering
        page.wait_for_timeout(400)

        search_results = page.locator("#search-results")
        is_visible = search_results.is_visible()
        log_result("Dropdown appears on typing", is_visible)

        # ─── Test 4: First item auto-highlighted ───
        print("\n--- Test 4: First item auto-highlighted ---")
        if is_visible:
            first_item = page.locator("#search-results .search-result").first
            has_highlight = "highlighted" in (first_item.get_attribute("class") or "")
            first_symbol = first_item.get_attribute("data-symbol")
            log_result("First dropdown item highlighted", has_highlight,
                       f"First item: {first_symbol}")
        else:
            log_result("First dropdown item highlighted", False, "Dropdown not visible")

        # ─── Test 5: Tab auto-completes and triggers analysis ───
        print("\n--- Test 5: Tab auto-completes + analyzes ---")
        # Press Tab - should auto-select top result and trigger analysis
        ticker_input.press("Tab")
        # Wait for analysis to start
        page.wait_for_timeout(500)

        # Check input was filled with the symbol
        ticker_value = page.input_value("#ticker-input")
        log_result("Tab fills ticker from dropdown", len(ticker_value) > 0 and ticker_value != "MSF",
                   f"Input value: '{ticker_value}'")

        # Wait for the chart to appear (analysis triggered)
        try:
            page.wait_for_selector("#stock-chart .plot-container, .js-plotly-plot", timeout=15000)
            log_result("Tab triggers analysis (chart appears)", True)
        except Exception as e:
            log_result("Tab triggers analysis (chart appears)", False, str(e)[:80])

        # Check results section is visible
        results_visible = page.locator("#results-section, .results-container, #stock-chart").first.is_visible()
        log_result("Results section visible after Tab", results_visible)

        # ─── Test 6: Enter key same behavior ───
        print("\n--- Test 6: Enter auto-completes + analyzes ---")
        # Clear and start fresh
        page.locator("#clear-btn").click()
        page.wait_for_timeout(500)

        ticker_input = page.locator("#ticker-input")
        ticker_input.click()
        ticker_input.fill("")
        # Use partial ticker that needs completion (NVD -> NVDA)
        ticker_input.type("NVD", delay=100)
        page.wait_for_timeout(400)

        # Verify dropdown appeared
        dropdown_visible = page.locator("#search-results").is_visible()
        log_result("Dropdown appears for second search", dropdown_visible)

        # Check what the first result is
        if dropdown_visible:
            first_result = page.locator("#search-results .search-result").first
            expected_enter = first_result.get_attribute("data-symbol")
        else:
            expected_enter = None

        # Press Enter
        ticker_input.press("Enter")
        page.wait_for_timeout(500)

        ticker_value_enter = page.input_value("#ticker-input")
        log_result("Enter fills ticker from dropdown",
                   expected_enter is not None and ticker_value_enter == expected_enter,
                   f"Expected '{expected_enter}', got '{ticker_value_enter}'")

        # Wait for chart
        try:
            page.wait_for_selector("#stock-chart .plot-container, .js-plotly-plot", timeout=15000)
            log_result("Enter triggers analysis (chart appears)", True)
        except Exception as e:
            log_result("Enter triggers analysis (chart appears)", False, str(e)[:80])

        # ─── Test 7: Arrow key navigation ───
        print("\n--- Test 7: Arrow key navigation ---")
        page.locator("#clear-btn").click()
        page.wait_for_timeout(500)

        ticker_input = page.locator("#ticker-input")
        ticker_input.click()
        ticker_input.fill("")
        # Use broad search term that returns multiple results
        ticker_input.type("Micro", delay=100)
        page.wait_for_timeout(400)

        # Press ArrowDown to move to second item
        ticker_input.press("ArrowDown")
        page.wait_for_timeout(100)

        # Check that second item is highlighted
        items = page.locator("#search-results .search-result")
        item_count = items.count()
        if item_count >= 2:
            second_class = items.nth(1).get_attribute("class") or ""
            first_class = items.nth(0).get_attribute("class") or ""
            log_result("ArrowDown highlights second item",
                       "highlighted" in second_class and "highlighted" not in first_class,
                       f"First: '{first_class}', Second: '{second_class}'")
        else:
            log_result("ArrowDown highlights second item", False,
                       f"Only {item_count} items in dropdown")

        # Press Enter to select second item
        if item_count >= 2:
            expected_symbol = items.nth(1).get_attribute("data-symbol")
            ticker_input.press("Enter")
            page.wait_for_timeout(300)
            selected_value = page.input_value("#ticker-input")
            log_result("Enter selects arrow-highlighted item",
                       selected_value == expected_symbol,
                       f"Expected '{expected_symbol}', got '{selected_value}'")

        # ─── Test 8: Comparison field - same Bloomberg behavior ───
        print("\n--- Test 8: Comparison field Tab behavior ---")
        # First, analyze a stock so comparison is available
        page.locator("#clear-btn").click()
        page.wait_for_timeout(500)
        ticker_input = page.locator("#ticker-input")
        ticker_input.click()
        ticker_input.fill("")
        ticker_input.type("MSFT", delay=80)
        page.wait_for_timeout(400)
        ticker_input.press("Enter")

        try:
            page.wait_for_selector("#stock-chart .plot-container, .js-plotly-plot", timeout=15000)
        except Exception:
            log_result("Pre-comparison: stock loaded", False, "Could not load MSFT")

        # Now type in comparison field
        compare_input = page.locator("#compare-input")
        compare_input.click()
        compare_input.fill("")
        # Use partial ticker that needs completion (AMZ -> AMZN)
        compare_input.type("AMZ", delay=100)
        page.wait_for_timeout(400)

        compare_dropdown = page.locator("#compare-results")
        compare_visible = compare_dropdown.is_visible()
        log_result("Comparison dropdown appears", compare_visible)

        expected_compare = None
        if compare_visible:
            # Check first item highlighted
            first_compare = page.locator("#compare-results .compare-result").first
            compare_highlight = "highlighted" in (first_compare.get_attribute("class") or "")
            expected_compare = first_compare.get_attribute("data-symbol")
            log_result("Comparison first item highlighted", compare_highlight)

        # Tab away from comparison
        compare_input.press("Tab")
        page.wait_for_timeout(500)

        compare_value = page.input_value("#compare-input")
        log_result("Tab fills comparison from dropdown",
                   expected_compare is not None and compare_value == expected_compare,
                   f"Expected '{expected_compare}', got '{compare_value}'")

        # ─── Test 9: Escape closes dropdown ───
        print("\n--- Test 9: Escape closes dropdown ---")
        page.locator("#clear-btn").click()
        page.wait_for_timeout(500)
        ticker_input = page.locator("#ticker-input")
        ticker_input.click()
        ticker_input.fill("")
        ticker_input.type("IBM", delay=100)
        page.wait_for_timeout(400)

        esc_dropdown_before = page.locator("#search-results").is_visible()
        ticker_input.press("Escape")
        page.wait_for_timeout(100)
        esc_dropdown_after = page.locator("#search-results").is_visible()
        log_result("Escape closes dropdown",
                   esc_dropdown_before and not esc_dropdown_after)

        # ─── Test 10: Blur auto-completes (click away) ───
        print("\n--- Test 10: Blur auto-completes + analyzes ---")
        page.locator("#clear-btn").click()
        page.wait_for_timeout(500)
        ticker_input = page.locator("#ticker-input")
        ticker_input.click()
        ticker_input.fill("")
        # Use partial ticker that needs completion (TSL -> TSLA)
        ticker_input.type("TSL", delay=80)
        page.wait_for_timeout(400)

        blur_dropdown = page.locator("#search-results").is_visible()
        expected_blur = None
        if blur_dropdown:
            expected_blur = page.locator("#search-results .search-result").first.get_attribute("data-symbol")

        # Click somewhere else to blur
        page.locator("body").click(position={"x": 10, "y": 10})
        page.wait_for_timeout(500)

        blur_value = page.input_value("#ticker-input")
        log_result("Blur auto-completes ticker",
                   expected_blur is not None and blur_value == expected_blur,
                   f"Expected '{expected_blur}', got '{blur_value}'")

        # Wait for analysis
        try:
            page.wait_for_selector("#stock-chart .plot-container, .js-plotly-plot", timeout=15000)
            log_result("Blur triggers analysis", True)
        except Exception as e:
            log_result("Blur triggers analysis", False, str(e)[:80])

        # ─── Test 11: No JS errors ───
        print("\n--- Test 11: JavaScript errors ---")
        log_result("No JavaScript errors", len(js_errors) == 0,
                   f"{len(js_errors)} errors: {'; '.join(js_errors[:3])}" if js_errors else "Clean")

        # Take final screenshot
        screenshot_path = "bloomberg_ux_test.png"
        page.screenshot(path=screenshot_path, full_page=True)
        print(f"\nScreenshot saved: {screenshot_path}")

        browser.close()

    # ─── Summary ───
    print("\n" + "=" * 60)
    passed = sum(1 for _, p, _ in RESULTS if p)
    failed = sum(1 for _, p, _ in RESULTS if not p)
    print(f"Results: {passed} passed, {failed} failed, {len(RESULTS)} total")

    if failed > 0:
        print("\nFailed tests:")
        for name, p, detail in RESULTS:
            if not p:
                print(f"  FAIL: {name} - {detail}")

    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(run_tests())
