Feature: 9.0 Dashboard UX Baseline
    As anyone using the usage dashboard, with a mouse, a keyboard, a phone-width window or assistive technology
    I want every screen to tell me what state it is in and to behave the same predictable way
    So that I am never left guessing whether the page is loading, empty or broken, and never change a quota by accident


# --------------------------------------------------------------------------------------------------
# UX Baseline
# --------------------------------------------------------------------------------------------------
# Note: reference screens — My Usage (S-01), User Detail (S-03)

Scenario: [TOR-09-upSE1xN] The dashboard shall render an explicit loading state, distinguishable by visible text, while a data-bearing screen loads
    Given "pat@example.com" is signed in
    And the test latency switch delays the data source by 3 seconds
    When the person navigates to My Usage (S-01)
    Then within 1 second the main content region should contain the visible text "Loading your usage…"
    And no stat card value should be visible until the load completes

Scenario: [TOR-09-9Orj70T] The dashboard shall render an explicit empty state, distinguishable by visible text, when a data-bearing screen has nothing to show
    Given "pat@example.com" is signed in with a $25.00 quota and has no usage in 2026-11
    When the person views My Usage (S-01) for "2026-11"
    Then the main content region should contain the visible text "No usage in 2026-11"
    And the main content region should contain the text "Narration you request through the peak-media tools in Claude Code appears here. Your quota is $25.00 for the month."
    And the main content region should contain a "Go to current month" button
    And the text "Loading your usage…" should not be visible

Scenario: [TOR-09-gadlg25] The dashboard shall render an explicit error state, distinguishable by visible text, when a data-bearing screen fails to load
    Given "pat@example.com" is signed in
    And the test fault switch forces the data source to fail
    When the person navigates to My Usage (S-01)
    Then the main content region should contain the visible text "Couldn't load your usage. Check your connection and try again."
    And the main content region should contain a "Retry" button
    And no stat card should be visible

Scenario: [TOR-09-ZbKGrFs] The dashboard shall render an explicit populated state on a data-bearing screen that has data
    Given "pat@example.com" is signed in and has usage in the current month
    When the person navigates to My Usage (S-01)
    Then the stat cards "Spent", "Quota", "Remaining", "Clips synthesized", "Cache hits" and "Resets" should be visible
    And none of the texts "Loading your usage…", "No usage in" and "Couldn't load your usage." should be visible

Scenario: [TOR-09-dty904j] The dashboard shall render the empty state of User Detail (S-03) in visible text while keeping the quota form available
    Given an administrator is signed in and "pat@example.com" has no usage in 2026-11
    When the administrator views User Detail (S-03) for "pat@example.com" and "2026-11"
    Then the main content region should contain the visible text "No usage for pat@example.com in 2026-11"
    And the main content region should contain the text "This user requested no narration in this month. You can still change their quota above."
    And the "Monthly quota (USD)" input and the "Save" button should be visible
    And the main content region should contain a "Back to Usage" link

Scenario: [TOR-09-CYbsdon] The dashboard shall render the error state of User Detail (S-03) in visible text with a "Retry" button
    Given an administrator is signed in
    And the test fault switch forces the data source to fail
    When the administrator navigates to User Detail (S-03) for "pat@example.com"
    Then the main content region should contain the visible text "Couldn't load usage for pat@example.com. Check your connection and try again."
    And the main content region should contain a "Retry" button

Scenario: [TOR-09-7aP7tWP] The dashboard shall allow every interactive control to be reached and operated by keyboard alone with no keyboard trap
    Given an administrator is on User Detail (S-03) for "pat@example.com" with the pointer unused
    When the administrator presses Tab repeatedly from the top of the page
    Then focus should reach, in document order, the navigation links, the "Sign out" button, the "Usage" breadcrumb link, the "Month" select, the "Monthly quota (USD)" input, the "Save" button and the "Use default…" button
    And pressing Enter on the focused "Save" button should submit the quota form
    And continuing to press Tab should move focus out of the page content and back to the first control

Scenario: [TOR-09-fjQhX70] The dashboard shall display a visible focus indicator on the focused control that is not hidden behind the app header
    Given "pat@example.com" is on My Usage (S-01) at a viewport height of 480 CSS px, scrolled to the bottom
    When the person presses Shift+Tab until each control has held focus once
    Then every focused control should have a computed outline or box-shadow that differs from its unfocused style
    And every focused control's bounding box should lie within the viewport and outside the app header's bounding box

Scenario: [TOR-09-YFn4LMC] The dashboard shall move focus into a modal dialog on open, keep Tab within it, and return focus to the invoking control on close
    Given an administrator is on User Detail (S-03) for "pat@example.com", whose quota override is $60.00
    When the administrator activates the "Use default…" button with the keyboard
    Then document.activeElement should be inside the element with role "alertdialog"
    When the administrator presses Tab four times
    Then document.activeElement should still be inside that dialog
    When the administrator presses Escape
    Then the dialog should not be visible
    And document.activeElement should be the "Use default…" button

Scenario: [TOR-09-pQcPvui] The dashboard shall associate a label with every form field
    Given an administrator is on User Detail (S-03) for "pat@example.com"
    When the form controls on the page are inspected
    Then the quota input should have the accessible name "Monthly quota (USD)"
    And the month select should have the accessible name "Month"
    And clicking the visible "Monthly quota (USD)" label should move focus to the quota input

Scenario: [TOR-09-RA5qSsd] The dashboard shall identify an invalid form field in text next to it that names the problem and the fix, and shall move focus to the first invalid field
    Given an administrator is on User Detail (S-03) for "pat@example.com", whose quota is $25.00
    When the administrator types "-5" in the "Monthly quota (USD)" input and clicks the "Save" button
    Then the "Monthly quota (USD)" input should have aria-invalid="true"
    And an element referenced by the input's aria-describedby should contain the text "Enter an amount of $0.00 or more, for example 60."
    And document.activeElement should be the "Monthly quota (USD)" input
    And "pat@example.com"'s my_usage result should still show quota "$25.00"

Scenario: [TOR-09-do58nOS] The dashboard shall require an explicit confirmation before an irreversible action, with the safe option as the default and Escape cancelling
    Given an administrator is on User Detail (S-03) for "pat@example.com", whose quota override is $60.00
    When the administrator activates the "Use default…" button
    Then a dialog with role "alertdialog" should be visible containing the text "Reset pat@example.com to the default quota?"
    And the dialog's first button should be "Cancel" and document.activeElement should be the "Cancel" button
    When the administrator presses Escape
    Then the dialog should not be visible
    And "pat@example.com"'s my_usage result should still show quota "$60.00"

Scenario: [TOR-09-TpkiHjX] The dashboard shall show a visible progress indicator within 1 second of starting any operation that takes longer than 1 second
    Given an administrator is on User Detail (S-03) for "pat@example.com"
    And the test latency switch delays the data source by 3 seconds
    When the administrator types "60" in the "Monthly quota (USD)" input and clicks the "Save" button
    Then within 1 second an element with role "progressbar" or role "status" should be visible
    And the "Save" button should be disabled until the operation completes

Scenario: [TOR-09-NtBVF41] The dashboard shall offer a cancel control for any operation that takes longer than 10 seconds
    Given "pat@example.com" is signed in
    And the test latency switch delays the data source by 15 seconds
    When the person navigates to My Usage (S-01) and 10 seconds pass
    Then a "Cancel" button should be visible next to the loading text
    When the person clicks the "Cancel" button
    Then the text "Loading your usage…" should not be visible
    And the main content region should contain the text "Loading was cancelled." and a "Retry" button

Scenario: [TOR-09-cTG2frO] The dashboard shall present every screen without horizontal scrolling, overlap or clipped controls at a viewport width of 320 CSS px
    Given an administrator is signed in and the viewport is 320 CSS px wide
    When the administrator views My Usage (S-01) and then User Detail (S-03) for a user with usage rows
    Then on each screen document.documentElement.scrollWidth should not exceed 320
    And every button, link, select and input on each screen should lie fully within the viewport width
    And no two controls' bounding boxes should overlap

Scenario: [TOR-09-1DjTmDv] The dashboard shall present every screen without horizontal scrolling, overlap or clipped controls at 200% zoom
    Given an administrator is signed in on a 1280 CSS px wide window zoomed to 200%
    When the administrator views My Usage (S-01) and then User Detail (S-03) for a user with usage rows
    Then on each screen document.documentElement.scrollWidth should not exceed the viewport width
    And every button, link, select and input on each screen should lie fully within the viewport width

Scenario: [TOR-09-BLJrVJ6] The dashboard shall render body text at a contrast ratio of at least 4.5:1, and large text at least 3:1, in both the light and the dark theme
    Given "pat@example.com" is on My Usage (S-01) with usage in the current month
    When the computed text and background colors of every visible text element are measured in the light theme and again in the dark theme
    Then every text element smaller than 24 CSS px (or 18.66 CSS px bold) should have a contrast ratio of at least 4.5:1
    And every larger text element should have a contrast ratio of at least 3:1

Scenario: [TOR-09-P5YL2JO] The dashboard shall render control boundaries and focus indicators at a contrast ratio of at least 3:1 against adjacent colors
    Given an administrator is on User Detail (S-03) for "pat@example.com"
    When the border colors of the "Monthly quota (USD)" input, the "Month" select and the "Save" button, and the focus indicator of each, are measured against their adjacent background in the light theme and again in the dark theme
    Then every measured contrast ratio should be at least 3:1

Scenario: [TOR-09-GFQ80Xs] The dashboard shall replace non-essential animation with an instant transition when the reduce-motion preference is set
    Given the browser emulates prefers-reduced-motion: reduce
    And an administrator is on User Detail (S-03) for "pat@example.com", whose quota override is $60.00
    When the administrator activates the "Use default…" button
    Then the dialog should be fully visible within 50 milliseconds
    And the computed animation-duration and transition-duration of the dialog and its overlay should each be at most 0.01s

Scenario: [TOR-09-9irho1R] The dashboard shall give every screen a unique document title
    Given an administrator is signed in
    When the administrator views My Usage (S-01) and then User Detail (S-03) for "pat@example.com"
    Then the document title of My Usage (S-01) should be "My usage · PeakMedia Gateway"
    And the document title of User Detail (S-03) should be "pat@example.com · PeakMedia Gateway"

Scenario: [TOR-09-hdlqc7J] The dashboard shall show on every screen exactly one visible H1 that matches the document title
    Given an administrator is signed in
    When the administrator views My Usage (S-01) and then User Detail (S-03) for "pat@example.com"
    Then My Usage (S-01) should have exactly one visible h1 element, with the text "My usage"
    And User Detail (S-03) should have exactly one visible h1 element, with the text "pat@example.com"

Scenario: [TOR-09-PVdyo7H] The dashboard shall mark the current item of the primary navigation with aria-current
    Given an administrator is signed in
    When the administrator views My Usage (S-01) and then User Detail (S-03) for "pat@example.com"
    Then on My Usage (S-01) only the "My usage" navigation link should have aria-current="page"
    And on User Detail (S-03) only the "Usage" navigation link should have an aria-current attribute
