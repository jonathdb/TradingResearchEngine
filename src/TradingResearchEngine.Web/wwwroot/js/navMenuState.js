/**
 * NavMenu state persistence via localStorage.
 * Stores expanded/collapsed state of MudNavGroup sections.
 */
window.navMenuState = {
    /**
     * Gets the expanded state for a given section name.
     * @param {string} sectionName - The key identifying the nav group.
     * @returns {boolean} - True if expanded, false if collapsed. Defaults to true if not set.
     */
    get: function (sectionName) {
        const value = localStorage.getItem("navMenu_" + sectionName);
        if (value === null) return true; // default expanded
        return value === "true";
    },

    /**
     * Sets the expanded state for a given section name.
     * @param {string} sectionName - The key identifying the nav group.
     * @param {boolean} expanded - Whether the section is expanded.
     */
    set: function (sectionName, expanded) {
        localStorage.setItem("navMenu_" + sectionName, expanded.toString());
    }
};
