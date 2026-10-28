'use client';

import Link from 'next/link';
import { PureComponent } from 'react';
import HomeOutlinedIcon from '@mui/icons-material/HomeOutlined';
import CurrencyBitcoinIcon from '@mui/icons-material/CurrencyBitcoin';
import GroupsOutlinedIcon from '@mui/icons-material/GroupsOutlined';
import DashboardOutlinedIcon from '@mui/icons-material/DashboardOutlined';
import { Box, ListItemButton, ListItemIcon, ListItemText } from '@mui/material';
import type { NavigationProps } from './NavigationProps';
import type { NavigationState } from './NavigationState';

export default class Navigation extends PureComponent<
  NavigationProps,
  NavigationState
> {
  public override state: NavigationState = { pathname: null };

  private readonly updatePathname = (): void => {
    this.setState({ pathname: window.location.pathname });
  };

  public override componentDidMount(): void {
    window.addEventListener('popstate', this.updatePathname);
    window.addEventListener('quantara:route-change', this.updatePathname);
    this.updatePathname();
  }

  public override componentWillUnmount(): void {
    window.removeEventListener('popstate', this.updatePathname);
    window.removeEventListener('quantara:route-change', this.updatePathname);
  }

  public override render() {
    const { pathname } = this.state;
    const items = [
      { href: '/', label: 'Home', icon: <HomeOutlinedIcon fontSize="small" /> },
      {
        href: '/dashboard',
        label: 'Dashboard',
        icon: <DashboardOutlinedIcon fontSize="small" />,
      },
      {
        href: '/daily-brief',
        label: 'Daily Brief',
        icon: <GroupsOutlinedIcon fontSize="small" />,
      },
      {
        href: '/crypto',
        label: 'Crypto',
        icon: <CurrencyBitcoinIcon fontSize="small" />,
      },
    ];

    return (
      <Box
        component="nav"
        aria-label="Main navigation"
        sx={{
          width: this.props.compact ? 'auto' : '100%',
          p: this.props.compact ? 0 : 2,
        }}
      >
        {!this.props.compact && (
          <Box
            sx={{
              px: 2,
              py: 2,
              mb: 2,
              fontFamily: 'var(--font-display)',
              fontSize: 24,
              fontWeight: 900,
              bgcolor: 'var(--color-mint)',
              borderRadius: 'var(--radius-control)',
            }}
          >
            Quantara
          </Box>
        )}
        <Box
          sx={{
            display: 'flex',
            flexDirection: this.props.compact ? 'row' : 'column',
            gap: this.props.compact ? 0.75 : 1,
          }}
        >
          {items.map((item) => {
            const selected =
              pathname === item.href ||
              (pathname === '/team' && item.href === '/daily-brief');

            return (
              <ListItemButton
                key={item.href}
                component={Link}
                href={item.href}
                selected={selected}
                onNavigate={() => {
                  this.setState({ pathname: item.href });
                  this.props.onNavigate?.();
                }}
                aria-current={selected ? 'page' : undefined}
                sx={{
                  minHeight: 44,
                  px: this.props.compact ? 1.5 : undefined,
                  flexGrow: 0,
                  border: '1px solid',
                  borderColor: selected ? 'primary.main' : 'transparent',
                  borderRadius: 'var(--radius-control)',
                  color: selected ? 'primary.contrastText' : 'text.primary',
                  '&:hover': { bgcolor: 'var(--color-mint)' },
                  '&.Mui-selected': { bgcolor: 'primary.main' },
                  '&.Mui-selected:hover': { bgcolor: 'primary.dark' },
                  '&.Mui-focusVisible': {
                    outline: '2px solid',
                    outlineColor: 'info.main',
                    outlineOffset: 2,
                  },
                }}
              >
                <ListItemIcon
                  sx={{
                    minWidth: this.props.compact ? 28 : 36,
                    color: 'inherit',
                  }}
                >
                  {item.icon}
                </ListItemIcon>
                <ListItemText
                  primary={item.label}
                  slotProps={{
                    primary: { sx: { fontSize: 14, fontWeight: 800 } },
                  }}
                />
              </ListItemButton>
            );
          })}
        </Box>
      </Box>
    );
  }
}
