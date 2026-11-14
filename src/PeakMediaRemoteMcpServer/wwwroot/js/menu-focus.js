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
