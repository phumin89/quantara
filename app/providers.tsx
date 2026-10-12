"use client";

import { AppRouterCacheProvider } from "@mui/material-nextjs/v16-appRouter";
import CssBaseline from "@mui/material/CssBaseline";
import { createTheme, ThemeProvider } from "@mui/material/styles";
import type { ReactNode } from "react";

const theme = createTheme({
  palette: {
    mode: "light",
    primary: { main: "#141414", contrastText: "#ffffff" },
    text: { primary: "#141414", secondary: "#707070" },
    background: { default: "#ffffff", paper: "#ffffff" },
    success: { main: "#166534", light: "#dcfce7" },
    warning: { main: "#92400e", light: "#fef3c7" },
    error: { main: "#991b1b", light: "#fee2e2" },
    info: { main: "#1e40af", light: "#dbeafe" },
  },
  typography: {
    fontFamily: 'Saans, -apple-system, "Helvetica Neue", Arial, sans-serif',
    allVariants: { letterSpacing: 0 },
    body1: { fontSize: "1rem", lineHeight: 1.38, fontWeight: 456 },
    h1: { fontSize: "1.5rem", lineHeight: 1.25, fontWeight: 652 },
  },
  breakpoints: { values: { xs: 0, sm: 600, md: 768, lg: 1024, xl: 1280 } },
  shape: { borderRadius: 8 },
  components: {
    MuiDrawer: {
      styleOverrides: {
        paper: {
          border: 0,
          borderRight: "1px solid #f0f0f0",
          boxShadow: "none",
        },
      },
    },
    MuiButtonBase: {
      defaultProps: { disableRipple: true },
      styleOverrides: {
        root: {
          "&.Mui-focusVisible": {
            outline: "2px solid #141414",
            outlineOffset: 3,
          },
        },
      },
    },
    MuiButton: { styleOverrides: { root: { borderRadius: 6 } } },
    MuiListItemButton: { styleOverrides: { root: { borderRadius: 6 } } },
    MuiToggleButton: { styleOverrides: { root: { borderRadius: 6 } } },
    MuiOutlinedInput: { styleOverrides: { root: { borderRadius: 6 } } },
    MuiChip: { styleOverrides: { root: { borderRadius: 4 } } },
    MuiPaper: { styleOverrides: { root: { borderRadius: 8 } } },
    MuiTableHead: {
      styleOverrides: {
        root: {
          backgroundColor: "#141414",
          "& .MuiTableCell-root": {
            backgroundColor: "#141414",
            color: "#ffffff",
          },
        },
      },
    },
  },
});

export default function Providers({ children }: { children: ReactNode }) {
  return (
    <AppRouterCacheProvider>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        {children}
      </ThemeProvider>
    </AppRouterCacheProvider>
  );
}
