// Theme/MudGreenTheme.cs
using MudBlazor;

namespace zxadocsui.Helpers;

public static class ThemeProvider
{
    public static MudTheme GeneralTheme = new MudTheme
    {
        PaletteLight = new PaletteLight
        {
            // Primary greens - your zxadocs brand colors
            Primary = "#1f7b4d",           // Deep green - main brand color
            PrimaryContrastText = "#ffffff", // White text on primary

            Secondary = "#44a56d",          // Lighter green for accents
            SecondaryContrastText = "#ffffff",

            Tertiary = "#2a5f40",           // Dark forest green
            TertiaryContrastText = "#ffffff",

            // Background colors
            Background = "#f4faf3",          // Soft mint background
            BackgroundGray = "#e8f3e9",       // Slightly darker mint for contrast
            Surface = "#ffffff",              // White surfaces

            // Text colors
            TextPrimary = "#195d3a",          // Dark green for primary text
            TextSecondary = "#2a5f40",         // Medium green for secondary text
            TextDisabled = "#8bb89b",          // Muted green for disabled

            // Action colors
            ActionDefault = "#2a5f40",         // Default action color
            ActionDisabled = "#8bb89b",         // Disabled action color
            ActionDisabledBackground = "#e0f0df", // Disabled background

            // Divider and border colors
            Divider = "#d4ead5",               // Light green divider
            DividerLight = "#e0f0df",           // Even lighter divider

            LinesDefault = "#cfe3d5",           // Default border color
            LinesInputs = "#cfe3d5",            // Input border color

            // Table colors
            TableLines = "#d4ead5",              // Table borders
            TableStriped = "#f0f9ef",             // Striped row background
            TableHover = "#e0f2df",                // Hover state

            // Appbar and drawer
            AppbarBackground = "#ffffff",         // White appbar
            AppbarText = "#195d3a",                // Dark green text

            DrawerBackground = "#ffffff",          // White drawer
            DrawerText = "#1e4a33",                 // Dark green drawer text
            DrawerIcon = "#2a7f4f",                  // Green icons

            // Overlay and backdrop
            OverlayDark = "rgba(25, 93, 58, 0.5)",  // Semi-transparent dark green
            OverlayLight = "rgba(255, 255, 255, 0.7)",

            // Status colors (keeping these standard but green-tinted)
            Info = "#1f7b4d",                       // Info uses primary green
            Success = "#1f7b4d",                     // Success uses primary green
            Warning = "#e6b85e",                     // Warm yellow for warnings
            Error = "#d9534f",                        // Standard red for errors

            // Dark variants for hover states
            PrimaryDarken = "#16633d",                // Darker primary for hover
            SecondaryDarken = "#358a55",               // Darker secondary for hover
            TertiaryDarken = "#1e4a33",                 // Darker tertiary
            InfoDarken = "#16633d",
            SuccessDarken = "#16633d",
            WarningDarken = "#d4a54a",
            ErrorDarken = "#c14b47",

            // Light variants for backgrounds
            PrimaryLighten = "#e0f2df",                // Very light primary for backgrounds
            SecondaryLighten = "#e8f7ea",
            TertiaryLighten = "#d4ead5",
            InfoLighten = "#e0f2df",
            SuccessLighten = "#e0f2df",
            WarningLighten = "#f9f1d5",
            ErrorLighten = "#fbe9e9",
        },

        PaletteDark = new PaletteDark
        {
            // Dark mode version - adjusted for dark backgrounds
            Primary = "#2a9d6e",                    // Brighter green for dark mode
            PrimaryContrastText = "#ffffff",
            Secondary = "#5bbd84",
            SecondaryContrastText = "#ffffff",
            Tertiary = "#3d8f5c",

            Background = "#1a2e24",                   // Dark green-black
            BackgroundGray = "#1e3a2f",
            Surface = "#1e3a2f",

            TextPrimary = "#e8f3e9",                   // Light mint
            TextSecondary = "#b8e0c4",
            TextDisabled = "#5f8b74",

            AppbarBackground = "#1a2e24",
            AppbarText = "#ffffff",

            DrawerBackground = "#1e3a2f",
            DrawerText = "#e8f3e9",
            DrawerIcon = "#b8e0c4",

            LinesDefault = "#2a5f40",
            LinesInputs = "#2a5f40",
            Divider = "#2a5f40",
            DividerLight = "#1e4a33",

            TableLines = "#2a5f40",
            TableStriped = "#1e3a2f",
            TableHover = "#2a5f40",

            Info = "#2a9d6e",
            Success = "#2a9d6e",
            Warning = "#e6b85e",
            Error = "#d9534f",
        },

        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",          // Rounded corners
            DrawerWidthLeft = "280px",             // Sidebar width
            DrawerWidthRight = "280px",
            AppbarHeight = "64px",                  // Header height
        },

        ZIndex = new ZIndex
        {
            AppBar = 1100,
            Drawer = 1000,
            Dialog = 1300,
            Popover = 1200,
            Snackbar = 1400,
            Tooltip = 1500
        }
    };
}