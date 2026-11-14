// Returns focus to the header's menu button after the navigation sheet closes (UX Baseline: a dialog
// returns focus to the control that opened it). Runs after the sheet's own close handling.
window.peakMenu = {
    restoreFocus: function () {
        setTimeout(function () {
            var button = document.querySelector(".menu-trigger button");
            if (button && getComputedStyle(button).display !== "none") {
                button.focus();
            }
        }, 0);
    }
};

// Returns focus to a dialog's invoking control after the dialog closes, unless focus already moved
// somewhere else on purpose (UX Baseline: Keyboard & focus).
window.peakFocus = {
    restore: function (selector) {
        setTimeout(function () {
            var target = document.querySelector(selector);
            var active = document.activeElement;
            var lost = !active || active === document.body || active.closest("[role='alertdialog']") !== null;
            if (target && lost) {
                target.focus();
            }
        }, 0);
    }
};

// Reads a form field's current value (used when a form is submitted, so the value on screen is the one saved).
window.peakForm = {
    value: function (element) {
        return element ? element.value : "";
    }
};
