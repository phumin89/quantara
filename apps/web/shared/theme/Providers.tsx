'use client';

import { AppRouterCacheProvider } from '@mui/material-nextjs/v16-appRouter';
import { PureComponent } from 'react';
import CssBaseline from '@mui/material/CssBaseline';
import { createTheme, ThemeProvider } from '@mui/material/styles';
import type { ProvidersProps } from './ProvidersProps';
import { LocalizationProvider } from '@mui/x-date-pickers/LocalizationProvider';
import { AdapterDayjs } from '@mui/x-date-pickers/AdapterDayjs';
import '../presentation/BangkokDate';

export default class Providers extends PureComponent<ProvidersProps> {
  private static readonly theme = createTheme({
    cssVariables: true,
    palette: {
      mode: 'dark',
      primary: {
        main: '#a0b0f4',
        light: '#b9c7ff',
        dark: '#8b9ce2',
        contrastText: '#0f1223',
      },
      secondary: {
        main: '#f4ba87',
        light: '#ffd1a9',
        dark: '#d99c6b',
        contrastText: '#20170f',
      },
      text: { primary: '#f4f1ed', secondary: '#e2e5ec' },
      background: { default: '#0e1017', paper: '#171a23' },
      divider: '#2a2e36',
      success: { main: '#a6d1bc', light: '#c3e5d4', contrastText: '#14241d' },
      warning: { main: '#f4ba87', light: '#ffd1a9', contrastText: '#20170f' },
      error: { main: '#f2a6a9', light: '#ffc2c4', contrastText: '#29151a' },
      info: { main: '#a0b0f4', light: '#b9c7ff', contrastText: '#0f1223' },
    },
    typography: {
      fontFamily: 'var(--font-body)',
      allVariants: { letterSpacing: 0 },
      body1: { fontSize: '1rem', lineHeight: 1.6, fontWeight: 400 },
      body2: { fontSize: '0.875rem', lineHeight: 1.6 },
      caption: { fontSize: '0.75rem', lineHeight: 1.5 },
      h1: {
        fontSize: '2rem',
        lineHeight: 1.25,
        fontFamily: 'var(--font-display)',
        fontWeight: 600,
        letterSpacing: '-0.02em',
      },
      h2: {
        fontFamily: 'var(--font-display)',
        fontSize: '1.44rem',
        lineHeight: 1.3,
        fontWeight: 600,
      },
      h3: {
        fontFamily: 'var(--font-display)',
        fontSize: '1.2rem',
        lineHeight: 1.4,
        fontWeight: 600,
      },
    },
    breakpoints: { values: { xs: 0, sm: 600, md: 768, lg: 1024, xl: 1280 } },
    shape: { borderRadius: 10 },
    components: {
      MuiTypography: {
        styleOverrides: {
          root: {
            '&:is(h1, h2, h3)': {
              fontFamily: 'var(--font-display)',
              fontWeight: 600,
            },
          },
        },
      },
      MuiDrawer: {
        styleOverrides: {
          paper: {
            border: 0,
            borderRight: '1px solid var(--border-panel)',
            backgroundColor: 'var(--surface-raised)',
            backdropFilter: 'blur(18px)',
            boxShadow: 'var(--shadow-overlay)',
          },
        },
      },
      MuiButtonBase: {
        defaultProps: { disableRipple: true },
        styleOverrides: {
          root: {
            '&.Mui-focusVisible': {
              outline: '2px solid var(--color-focus)',
              outlineOffset: 3,
            },
          },
        },
      },
      MuiButton: {
        styleOverrides: {
          root: {
            borderRadius: 'var(--radius-control)',
            minHeight: 44,
            textTransform: 'none',
            fontWeight: 600,
            boxShadow: 'none',
            transition:
              'background-color var(--dur-micro), border-color var(--dur-micro)',
            '&:hover, &:active': { boxShadow: 'none' },
            '@media (prefers-reduced-motion: reduce)': {
              transition: 'none',
            },
          },
          contained: { boxShadow: 'var(--glass-hi)' },
          outlined: {
            borderColor: 'var(--border-panel)',
            backgroundColor: 'var(--surface-raised)',
            borderWidth: 1,
          },
        },
      },
      MuiIconButton: {
        styleOverrides: {
          root: {
            '&:active': {
              backgroundColor: 'var(--surface-info)',
              boxShadow: 'inset 0 0 0 1px var(--border-panel)',
            },
            '&.Mui-disabled': { boxShadow: 'none' },
          },
        },
      },
      MuiListItemButton: {
        styleOverrides: { root: { borderRadius: 'var(--radius-control)' } },
      },
      MuiToggleButton: {
        styleOverrides: {
          root: {
            borderRadius: 'var(--radius-control)',
            fontWeight: 600,
            '&.Mui-selected': {
              color: 'var(--color-accent)',
              backgroundColor: 'var(--surface-info)',
            },
          },
        },
      },
      MuiOutlinedInput: {
        styleOverrides: {
          root: {
            borderRadius: 'var(--radius-control)',
            backgroundColor: 'var(--surface-raised)',
          },
        },
      },
      MuiChip: {
        styleOverrides: {
          root: { borderRadius: 'var(--radius-tag)', fontWeight: 500 },
          colorSuccess: {
            color: 'var(--color-success)',
            backgroundColor: 'var(--surface-success)',
          },
          colorWarning: {
            color: 'var(--color-warning)',
            backgroundColor: 'var(--surface-warning)',
          },
          colorError: {
            color: 'var(--color-error)',
            backgroundColor: 'var(--surface-error)',
          },
          colorInfo: {
            color: 'var(--color-info)',
            backgroundColor: 'var(--surface-info)',
          },
        },
      },
      MuiAlert: {
        styleOverrides: {
          root: {
            borderRadius: 'var(--radius-control)',
            alignItems: 'flex-start',
            '&.MuiAlert-standard.MuiAlert-colorSuccess': {
              backgroundColor: 'var(--surface-success)',
              color: 'var(--color-success)',
            },
            '&.MuiAlert-standard.MuiAlert-colorWarning': {
              backgroundColor: 'var(--surface-warning)',
              color: 'var(--color-warning)',
            },
            '&.MuiAlert-standard.MuiAlert-colorError': {
              backgroundColor: 'var(--surface-error)',
              color: 'var(--color-error)',
            },
            '&.MuiAlert-standard.MuiAlert-colorInfo': {
              backgroundColor: 'var(--surface-info)',
              color: 'var(--color-info)',
            },
          },
          message: { minWidth: 0, overflowWrap: 'anywhere' },
        },
      },
      MuiPaper: {
        styleOverrides: {
          root: {
            '--mui-palette-text-secondary': 'var(--color-panel-muted)',
            borderRadius: 'var(--radius-panel)',
            backgroundImage: 'none',
            backgroundColor: 'var(--surface-raised)',
          },
          outlined: {
            backgroundColor: 'var(--surface-panel)',
            borderColor: 'var(--border-panel)',
            borderWidth: 1,
            backdropFilter: 'blur(var(--glass-blur))',
            WebkitBackdropFilter: 'blur(var(--glass-blur))',
            boxShadow: 'var(--shadow-panel)',
            '& .MuiPaper-outlined': {
              backgroundColor: 'var(--surface-soft)',
              backdropFilter: 'none',
              WebkitBackdropFilter: 'none',
              boxShadow: 'none',
            },
          },
        },
      },
      MuiDialog: {
        styleOverrides: {
          paper: {
            backgroundColor: 'var(--surface-raised)',
            border: '1px solid var(--border-panel)',
            boxShadow: 'var(--shadow-overlay)',
          },
        },
      },
      MuiDialogTitle: {
        styleOverrides: {
          root: {
            fontWeight: 600,
            padding: 'var(--panel-padding)',
          },
        },
      },
      MuiDialogContent: {
        styleOverrides: {
          root: { padding: 'var(--panel-padding)' },
        },
      },
      MuiDialogActions: {
        styleOverrides: {
          root: { padding: 'var(--panel-padding)' },
        },
      },
      MuiPopover: {
        styleOverrides: {
          paper: {
            backgroundColor: 'var(--surface-raised)',
            border: '1px solid var(--border-panel)',
            boxShadow: 'var(--shadow-overlay)',
          },
        },
      },
      MuiTableHead: {
        styleOverrides: {
          root: {
            backgroundColor: 'var(--surface-raised)',
            '& .MuiTableCell-root': {
              backgroundColor: 'var(--surface-raised)',
              color: 'var(--color-ink-2)',
              fontWeight: 500,
            },
          },
        },
      },
    },
  });

  public override render(): React.ReactNode {
    const { children } = this.props;

    return (
      <AppRouterCacheProvider>
        <ThemeProvider theme={Providers.theme}>
          <CssBaseline />
          <LocalizationProvider
            dateAdapter={AdapterDayjs}
            adapterLocale="en"
          >
            {children}
          </LocalizationProvider>
        </ThemeProvider>
      </AppRouterCacheProvider>
    );
  }
}
