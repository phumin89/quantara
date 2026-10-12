"use client";
import Link from "next/link";
import { usePathname } from "next/navigation";
import HomeOutlinedIcon from "@mui/icons-material/HomeOutlined";
import CurrencyBitcoinIcon from "@mui/icons-material/CurrencyBitcoin";
import {
  Box,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
} from "@mui/material";

export default function Navigation({
  onNavigate,
}: {
  onNavigate?: () => void;
}) {
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
      <Box sx={{ px: 2, py: 2, mb: 3, fontSize: 24, fontWeight: 652 }}>
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
              {...(onNavigate ? { onClick: onNavigate } : {})}
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
