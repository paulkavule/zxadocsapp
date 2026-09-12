// wwwroot/js/sidebar.js
function initializeSidebar() {
  console.log("Initializing sidebar...");

  const sidebar = document.getElementById("sidebar");
  const mainContent = document.getElementById("mainContent");
  const toggleBtn = document.getElementById("toggleSidebar");
  const toggleIcon = document.getElementById("toggleIcon");
  const logoText = document.querySelector(".sidebar-logo-text");
  const menuLabels = document.querySelectorAll(".sidebar-menu-label");
  const menuTexts = document.querySelectorAll(".sidebar-menu-text");
  const badges = document.querySelectorAll(".sidebar-badge");
  const userInfo = document.querySelector(".sidebar-user-info");
  const submenuTexts = document.querySelectorAll(".sidebar-submenu-text");

  // reports submenu elements
  const reportsToggle = document.getElementById("reportsToggle");
  const reportsSubmenu = document.getElementById("reportsSubmenu");
  const reportsChevron = document.getElementById("reportsChevron");

  // contracts submenu elements (Templates + Drafts)
  const contractsToggle = document.getElementById("contractsToggle");
  const contractsSubmenu = document.getElementById("contractsSubmenu");
  const contractsChevron = document.getElementById("contractsChevron");

  let sidebarCollapsed = false;
  let reportsExpanded = false;
  let contractsExpanded = false;

  // Load saved state from localStorage
  try {
    const savedSidebarState = localStorage.getItem("zxadocs_sidebar_collapsed");
    if (savedSidebarState !== null) {
      sidebarCollapsed = savedSidebarState === "true";
      // Apply initial state
      if (sidebarCollapsed) {
        sidebar.classList.remove("w-72");
        sidebar.classList.add("w-20");
        mainContent.classList.remove("ml-72");
        mainContent.classList.add("ml-20");
        toggleIcon.classList.remove("fa-chevron-left");
        toggleIcon.classList.add("fa-chevron-right");

        if (logoText) logoText.classList.add("hidden");
        menuLabels.forEach((el) => el.classList.add("hidden"));
        menuTexts.forEach((el) => el.classList.add("hidden"));
        badges.forEach((el) => el.classList.add("hidden"));
        if (userInfo) userInfo.classList.add("hidden");
        submenuTexts.forEach((el) => el.classList.add("hidden"));
      }
    }

    const savedReportsState = localStorage.getItem("zxadocs_reports_expanded");
    // Guarded like the contracts block below: the Reports section is permission-gated, so
    // it may not be in the DOM at all, and an unguarded read here breaks the whole sidebar.
    if (savedReportsState !== null && reportsSubmenu) {
      reportsExpanded = savedReportsState === "true";
      if (reportsExpanded) {
        reportsSubmenu.classList.remove("max-h-0", "opacity-0");
        reportsSubmenu.classList.add("max-h-96", "opacity-100");
        reportsChevron.style.transform = "rotate(180deg)";
      }
    }

    const savedContractsState = localStorage.getItem("zxadocs_contracts_expanded");
    if (savedContractsState !== null && contractsSubmenu) {
      contractsExpanded = savedContractsState === "true";
      if (contractsExpanded) {
        contractsSubmenu.classList.remove("max-h-0", "opacity-0");
        contractsSubmenu.classList.add("max-h-96", "opacity-100");
        contractsChevron.style.transform = "rotate(180deg)";
      }
    }
  } catch (e) {
    console.warn("Could not load saved state", e);
  }

  // toggle main sidebar
  if (toggleBtn) {
    toggleBtn.addEventListener("click", function () {
      if (sidebarCollapsed) {
        // expand
        sidebar.classList.remove("w-20");
        sidebar.classList.add("w-72");
        mainContent.classList.remove("ml-20");
        mainContent.classList.add("ml-72");
        toggleIcon.classList.remove("fa-chevron-right");
        toggleIcon.classList.add("fa-chevron-left");

        // show hidden elements
        if (logoText) logoText.classList.remove("hidden");
        menuLabels.forEach((el) => el.classList.remove("hidden"));
        menuTexts.forEach((el) => el.classList.remove("hidden"));
        badges.forEach((el) => el.classList.remove("hidden"));
        if (userInfo) userInfo.classList.remove("hidden");
        submenuTexts.forEach((el) => el.classList.remove("hidden"));
      } else {
        // collapse
        sidebar.classList.remove("w-72");
        sidebar.classList.add("w-20");
        mainContent.classList.remove("ml-72");
        mainContent.classList.add("ml-20");
        toggleIcon.classList.remove("fa-chevron-left");
        toggleIcon.classList.add("fa-chevron-right");

        // hide text elements
        if (logoText) logoText.classList.add("hidden");
        menuLabels.forEach((el) => el.classList.add("hidden"));
        menuTexts.forEach((el) => el.classList.add("hidden"));
        badges.forEach((el) => el.classList.add("hidden"));
        if (userInfo) userInfo.classList.add("hidden");
        submenuTexts.forEach((el) => el.classList.add("hidden"));
      }
      sidebarCollapsed = !sidebarCollapsed;

      // Save state to localStorage
      try {
        localStorage.setItem("zxadocs_sidebar_collapsed", sidebarCollapsed);
      } catch (e) {
        console.warn("Could not save sidebar state", e);
      }
    });
  }

  // reports submenu toggle
  if (reportsToggle) {
    reportsToggle.addEventListener("click", function (e) {
      e.preventDefault();
      if (reportsExpanded) {
        // collapse submenu
        reportsSubmenu.classList.remove("max-h-96", "opacity-100");
        reportsSubmenu.classList.add("max-h-0", "opacity-0");
        reportsChevron.style.transform = "rotate(0deg)";
      } else {
        // expand submenu
        reportsSubmenu.classList.remove("max-h-0", "opacity-0");
        reportsSubmenu.classList.add("max-h-96", "opacity-100");
        reportsChevron.style.transform = "rotate(180deg)";
      }
      reportsExpanded = !reportsExpanded;

      // Save state to localStorage
      try {
        localStorage.setItem("zxadocs_reports_expanded", reportsExpanded);
      } catch (e) {
        console.warn("Could not save reports state", e);
      }
    });
  }

  // contracts submenu toggle (Templates + Drafts)
  if (contractsToggle) {
    contractsToggle.addEventListener("click", function (e) {
      e.preventDefault();
      if (contractsExpanded) {
        contractsSubmenu.classList.remove("max-h-96", "opacity-100");
        contractsSubmenu.classList.add("max-h-0", "opacity-0");
        contractsChevron.style.transform = "rotate(0deg)";
      } else {
        contractsSubmenu.classList.remove("max-h-0", "opacity-0");
        contractsSubmenu.classList.add("max-h-96", "opacity-100");
        contractsChevron.style.transform = "rotate(180deg)";
      }
      contractsExpanded = !contractsExpanded;

      try {
        localStorage.setItem("zxadocs_contracts_expanded", contractsExpanded);
      } catch (e) {
        console.warn("Could not save contracts state", e);
      }
    });
  }

  // Handle navigation events to maintain active states
  window.addEventListener("blazorNavigation", function () {
    // Re-run active class logic if needed
    console.log("Navigation occurred");
  });
}
