(function () {
  // get elements
  console.log(" -- -- -- -- -- -- -- -- -- -->");
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

  let sidebarCollapsed = false;
  let reportsExpanded = false; // start collapsed

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
    });
  }

  // initial state: submenu closed
  if (reportsSubmenu) {
    reportsSubmenu.classList.add("max-h-0", "opacity-0");
  }
})();
