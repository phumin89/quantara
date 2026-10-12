"use client";
import { useState, type ReactNode } from "react";
import MenuIcon from "@mui/icons-material/Menu";
import {
  Box,
  Drawer,
  IconButton,
  useMediaQuery,
  useTheme,
} from "@mui/material";
import Navigation from "./Navigation";

const drawerWidth = 240;
export default function AppShell({ children }: { children: ReactNode }) {
  const theme = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down("md"));
  const [open, setOpen] = useState(false);
  const navigation = (
    <Navigation
      onNavigate={() => {
        setOpen(false);
      }}
    />
  );
  return (
    <Box sx={{ display: "flex", minHeight: "100dvh", width: "100%" }}>
      {isMobile ? (
        <Drawer
          open={open}
          onClose={() => {
            setOpen(false);
          }}
          ModalProps={{ keepMounted: true }}
          aria-label="Navigation menu"
          sx={{
            "& .MuiDrawer-paper": {
              width: drawerWidth,
              boxSizing: "border-box",
            },
          }}
        >
          {navigation}
        </Drawer>
      ) : (
        <Drawer
          variant="permanent"
          open
          sx={{
            width: drawerWidth,
            flexShrink: 0,
            "& .MuiDrawer-paper": {
              width: drawerWidth,
              boxSizing: "border-box",
            },
          }}
        >
          {navigation}
        </Drawer>
      )}
      <Box
        component="main"
        sx={{ flexGrow: 1, minWidth: 0, p: { xs: 3, md: 6 } }}
      >
        {isMobile && (
          <IconButton
            onClick={() => {
              setOpen(true);
            }}
            aria-label="Open menu"
            aria-expanded={open}
            sx={{ mb: 4, color: "text.primary", width: 44, height: 44 }}
          >
            <MenuIcon />
          </IconButton>
        )}
        {children}
      </Box>
    </Box>
  );
}
