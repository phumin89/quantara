"use client";
import { useState, type ReactNode } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import MenuIcon from "@mui/icons-material/Menu";
import HomeOutlinedIcon from "@mui/icons-material/HomeOutlined";
import CurrencyBitcoinIcon from "@mui/icons-material/CurrencyBitcoin";
import {
  Box,
  Drawer,
  IconButton,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  useMediaQuery,
  useTheme,
} from "@mui/material";
const drawerWidth = 240;
function Navigation({ onNavigate }: { onNavigate?: () => void }) {
  const pathname = usePathname();
  const items = [
    { href: "/", label: "Home", icon: <HomeOutlinedIcon fontSize="small" /> },
    {
      href: "/crypto",
      label: "Crypto",
      icon: <CurrencyBitcoinIcon fontSize="small" />,
    },
  ];
  return (
    <Box
      component="nav"
      aria-label="Main navigation"
      sx={{ width: "100%", p: 2 }}
    >
      <Box
        component="div"
        sx={{ px: 2, py: 2, mb: 3, fontSize: 24, fontWeight: 652 }}
      >
        Quantara
      </Box>
      <List
        disablePadding
        sx={{ display: "flex", flexDirection: "column", gap: 1 }}
      >
        {items.map((item) => {
          const selected = pathname === item.href;
          return (
            <ListItemButton
              key={item.href}
              component={Link}
              href={item.href}
              selected={selected}
              onClick={onNavigate}
              aria-current={selected ? "page" : undefined}
              sx={{
                minHeight: 44,
                "&.Mui-selected": { bgcolor: "#f3f3f3" },
                "&.Mui-selected:hover": { bgcolor: "#f0f0f0" },
              }}
            >
              <ListItemIcon sx={{ minWidth: 36, color: "inherit" }}>
                {item.icon}
              </ListItemIcon>
              <ListItemText
                primary={item.label}
                slotProps={{
                  primary: { sx: { fontSize: 14, fontWeight: 600 } },
                }}
              />
            </ListItemButton>
          );
        })}
      </List>
    </Box>
  );
}
export default function AppShell({ children }: { children: ReactNode }) {
  const theme = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down("md"));
  const [open, setOpen] = useState(false);
  const navigation = <Navigation onNavigate={() => setOpen(false)} />;
  return (
    <Box sx={{ display: "flex", minHeight: "100dvh", width: "100%" }}>
      {isMobile ? (
        <Drawer
          open={open}
          onClose={() => setOpen(false)}
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
            onClick={() => setOpen(true)}
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
