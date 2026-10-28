'use client';

import { Component } from 'react';
import NextLink from 'next/link';
import MenuIcon from '@mui/icons-material/Menu';
import { Box, Drawer, IconButton, Link, Typography } from '@mui/material';
import Navigation from './Navigation';
import type { AppShellProps } from './AppShellProps';
import type { AppShellState } from './AppShellState';

export default class AppShell extends Component<AppShellProps, AppShellState> {
  private static readonly drawerWidth = 240;

  public override state: AppShellState = { isMobile: false, open: false };

  private media: MediaQueryList | null = null;

  private readonly updateViewport = (): void => {
    this.setState({ isMobile: this.media?.matches ?? false, open: false });
  };

  public override componentDidMount(): void {
    this.media = window.matchMedia('(max-width: 1023.98px)');
    this.media.addEventListener('change', this.updateViewport);
    this.updateViewport();
  }

  public override componentWillUnmount(): void {
    this.media?.removeEventListener('change', this.updateViewport);
  }

  public override render() {
    const { children, accountContext, sidebar } = this.props;
    const { isMobile, open } = this.state;
    const navigation = (
      <Navigation
        onNavigate={() => {
          this.setState({ open: false });
        }}
      />
    );

    return (
      <Box
        sx={{
          display: 'flex',
          flexDirection: 'column',
          minHeight: '100dvh',
          width: '100%',
        }}
      >
        <Link
          href="#main-content"
          underline="none"
          sx={{
            position: 'fixed',
            left: 8,
            top: 8,
            zIndex: 1200,
            minHeight: 44,
            px: 2,
            display: 'flex',
            alignItems: 'center',
            bgcolor: 'var(--surface-raised)',
            color: 'text.primary',
            border: '1px solid',
            borderColor: 'var(--border-panel)',
            borderRadius: 'var(--radius-control)',
            boxShadow: 'var(--shadow-panel)',
            fontWeight: 800,
            transform: 'translateY(-150%)',
            '&:focus': { transform: 'translateY(0)' },
          }}
        >
          Skip to main content
        </Link>
        <Box
          component="header"
          sx={{
            height: 64,
            boxSizing: 'border-box',
            borderBottom: '1px solid',
            borderColor: 'var(--border-panel)',
            bgcolor: 'var(--surface-header)',
            backdropFilter: 'blur(18px)',
            position: 'sticky',
            top: 0,
            zIndex: 1100,
          }}
        >
          <Box
            sx={{
              display: 'flex',
              alignItems: 'center',
              gap: { xs: 0.5, sm: 3 },
              height: '100%',
              maxWidth: 1440,
              mx: 'auto',
              px: 'var(--page-gutter)',
            }}
          >
            <IconButton
              onClick={() => {
                this.setState({ open: true });
              }}
              aria-label="Open menu"
              aria-expanded={open}
              sx={{
                color: 'text.primary',
                width: 44,
                height: 44,
                flexShrink: 0,
                borderRadius: 'var(--radius-control)',
                bgcolor: 'var(--surface-soft)',
                '&:hover': { bgcolor: 'var(--color-mint)' },
                display: { xs: 'inline-flex', lg: 'none' },
              }}
            >
              <MenuIcon />
            </IconButton>
            <Link
              component={NextLink}
              href="/"
              aria-label="Quantara home"
              underline="none"
              sx={{
                color: 'text.primary',
                fontFamily: 'var(--font-display)',
                fontSize: { xs: 22, sm: 24 },
                fontWeight: 650,
                letterSpacing: '-0.03em',
                flexShrink: 0,
                minHeight: 44,
                display: 'inline-flex',
                alignItems: 'center',
              }}
            >
              Quantara
            </Link>
            <Box sx={{ display: { xs: 'none', lg: 'block' } }}>
              <Navigation compact />
            </Box>
            <Box
              sx={{
                ml: 'auto',
                minWidth: 0,
                maxWidth: { xs: 80, sm: 200 },
                px: { xs: 0, sm: 1.5 },
                py: { xs: 0, sm: 0.5 },
                borderRadius: 'var(--radius-control)',
                bgcolor: { xs: 'transparent', sm: 'var(--surface-soft)' },
                textAlign: 'right',
                overflowWrap: 'anywhere',
              }}
            >
              <Typography sx={{ fontSize: 12, fontWeight: 800 }}>
                Bitkub TH
              </Typography>
              <Typography
                sx={{
                  fontSize: { xs: 11, sm: 12 },
                  color: 'text.secondary',
                  whiteSpace: 'nowrap',
                }}
                title="Research workspace context; wallet account identity must be checked in its source snapshot."
              >
                {accountContext ? (
                  <>
                    <Box
                      component="span"
                      sx={{ display: { xs: 'none', sm: 'inline' } }}
                    >
                      Workspace{' '}
                    </Box>
                    {'…' + accountContext.slice(-6)}
                  </>
                ) : (
                  'Unconfirmed'
                )}
              </Typography>
            </Box>
            {sidebar && (
              <Box
                id="team-chat-toggle"
                sx={{
                  minWidth: { xs: 68, sm: 116 },
                  minHeight: 44,
                  flexShrink: 0,
                }}
              />
            )}
          </Box>
        </Box>
        {isMobile ? (
          <Drawer
            open={open}
            onClose={() => {
              this.setState({ open: false });
            }}
            ModalProps={{ keepMounted: true }}
            aria-label="Navigation menu"
            sx={{
              '& .MuiDrawer-paper': {
                width: AppShell.drawerWidth,
                boxSizing: 'border-box',
                bgcolor: 'var(--surface-panel)',
                borderColor: 'var(--border-panel)',
                borderRadius: '0 var(--radius-panel) var(--radius-panel) 0',
                boxShadow: 'var(--shadow-panel)',
              },
            }}
          >
            {navigation}
          </Drawer>
        ) : null}
        <Box
          sx={{
            flex: 1,
            minWidth: 0,
          }}
        >
          <Box
            component="main"
            id="main-content"
            tabIndex={-1}
            sx={{
              flex: 1,
              minWidth: 0,
              width: '100%',
              maxWidth: 1344,
              mx: 'auto',
              px: 'var(--page-gutter)',
              pt: 'var(--page-gutter)',
              pb: 6,
              scrollMarginTop: 80,
            }}
          >
            {children}
            <Box
              component="footer"
              aria-label="Background credits"
              data-credits
              sx={{
                mt: 4,
                width: 'fit-content',
                px: 1.5,
                py: 1,
                fontSize: 12,
                color: 'text.secondary',
                bgcolor: 'var(--surface-raised)',
                borderRadius: 'var(--radius-control)',
              }}
            >
              Photo:{' '}
              <Link
                href="https://commons.wikimedia.org/wiki/File:Vallee-brouillard.jpg"
                color="inherit"
              >
                Thomas Bresson
              </Link>{' '}
              /{' '}
              <Link
                href="https://creativecommons.org/licenses/by/3.0/"
                color="inherit"
              >
                CC BY 3.0
              </Link>{' '}
              (adapted)
            </Box>
          </Box>
        </Box>
        {sidebar}
      </Box>
    );
  }
}
