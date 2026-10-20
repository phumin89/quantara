'use client';

import Link from 'next/link';
import { PureComponent } from 'react';
import HomeOutlinedIcon from '@mui/icons-material/HomeOutlined';
import CurrencyBitcoinIcon from '@mui/icons-material/CurrencyBitcoin';
import GroupsOutlinedIcon from '@mui/icons-material/GroupsOutlined';
import ForumOutlinedIcon from '@mui/icons-material/ForumOutlined';
import DashboardOutlinedIcon from '@mui/icons-material/DashboardOutlined';
import {
  Box,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
} from '@mui/material';
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
    this.updatePathname();
  }

  public override componentWillUnmount(): void {
    window.removeEventListener('popstate', this.updatePathname);
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
        sx={{ width: '100%', p: 2 }}
      >
        <Box sx={{ px: 2, py: 2, mb: 3, fontSize: 24, fontWeight: 652 }}>
          Quantara
        </Box>
        <List
          disablePadding
          sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}
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
                  '&.Mui-selected': { bgcolor: '#f3f3f3' },
                  '&.Mui-selected:hover': { bgcolor: '#f0f0f0' },
                }}
              >
                <ListItemIcon sx={{ minWidth: 36, color: 'inherit' }}>
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
          {this.props.onOpenTeamChat && (
            <ListItemButton
              onClick={this.props.onOpenTeamChat}
              aria-haspopup="dialog"
              aria-expanded={this.props.teamChatOpen ?? false}
              aria-controls="team-chat-dialog"
              sx={{ minHeight: 44 }}
            >
              <ListItemIcon sx={{ minWidth: 36, color: 'inherit' }}>
                <ForumOutlinedIcon fontSize="small" />
              </ListItemIcon>
              <ListItemText
                primary="Team chat"
                slotProps={{
                  primary: { sx: { fontSize: 14, fontWeight: 600 } },
                }}
              />
            </ListItemButton>
          )}
        </List>
      </Box>
    );
  }
}
