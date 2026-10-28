import { PureComponent } from 'react';
import { Box, Paper, Typography } from '@mui/material';
import type { DashboardChartProps } from './DashboardChartProps';
import { DashboardPresentation } from '../../presentation/DashboardPresentation';

export default class DashboardChart extends PureComponent<DashboardChartProps> {
  public override render() {
    const { title, points, kind, source, empty } = this.props;
    const values = points.flatMap((point) =>
      point.value === null ? [] : [point.value],
    );
    const minimum = Math.min(0, ...values);
    const maximum = Math.max(0, ...values) || (minimum === 0 ? 1 : 0);
    const span = maximum - minimum;
    let path = '';
    let connected = false;
    for (const [index, point] of points.entries()) {
      if (point.value === null) {
        connected = false;
        continue;
      }

      path += `${connected ? 'L' : 'M'}${String(this.x(index))},${String(DashboardChart.y(point.value, minimum, span))} `;
      connected = true;
    }

    return (
      <Paper
        component="section"
        variant="outlined"
        sx={(theme) => ({
          p: 'var(--panel-padding)',
          minWidth: 0,
          containerType: 'inline-size',
          borderRadius: 'var(--radius-panel)',
          '--chart-grid': theme.palette.divider,
          '--chart-ink': theme.palette.text.secondary,
          '--chart-series': theme.palette.info.main,
          '--chart-gain': theme.palette.success.main,
          '--chart-loss': theme.palette.error.main,
          '& svg text': { fontSize: 16 },
          '@container (max-width: 480px)': {
            '& svg text': { fontSize: 20 },
          },
          '@container (max-width: 384px)': {
            '& svg text': { fontSize: 26 },
            '& .chart-intermediate-date': { display: 'none' },
          },
          '@container (max-width: 296px)': {
            '& svg text': { fontSize: 31 },
          },
        })}
      >
        <Typography
          component="h2"
          sx={{ fontSize: 20, fontWeight: 800 }}
        >
          {title}
        </Typography>
        {values.length === 0 ? (
          <Box sx={{ minHeight: 72, display: 'grid', alignContent: 'center' }}>
            <Typography color="text.secondary">{empty}</Typography>
          </Box>
        ) : (
          <Box
            component="figure"
            sx={{ m: 0, mt: 2 }}
          >
            <Box
              role="region"
              aria-label={title + ' chart'}
              tabIndex={0}
              sx={{ overflowX: 'auto' }}
            >
              <svg
                viewBox="0 0 640 260"
                role="img"
                aria-label={
                  title + '. THB. Missing observations are gaps, not zero.'
                }
                style={{ width: '100%', height: 'auto', display: 'block' }}
              >
                <title>{title}</title>
                <desc>
                  Retained source observations in THB. Each point has its date
                  and amount. Missing records are not interpolated.
                </desc>
                {[minimum, minimum + span / 2, maximum].map((value, index) => (
                  <g key={index}>
                    <line
                      x1="84"
                      x2="612"
                      y1={DashboardChart.y(value, minimum, span)}
                      y2={DashboardChart.y(value, minimum, span)}
                      stroke="var(--chart-grid)"
                    />
                    <text
                      x="76"
                      y={DashboardChart.y(value, minimum, span) + 4}
                      textAnchor="end"
                      fill="var(--chart-ink)"
                    >
                      {DashboardChart.tick(value)}
                    </text>
                  </g>
                ))}
                {kind === 'line' && (
                  <path
                    d={path}
                    fill="none"
                    stroke="var(--chart-series)"
                    strokeWidth="2.5"
                  />
                )}
                {points.map((point, index) => (
                  <g key={point.label}>
                    {point.value !== null &&
                      (kind === 'bar' ? (
                        <rect
                          x={this.x(index) - 18}
                          y={Math.min(
                            DashboardChart.y(0, minimum, span),
                            DashboardChart.y(point.value, minimum, span),
                          )}
                          width="36"
                          height={Math.max(
                            1,
                            Math.abs(
                              DashboardChart.y(point.value, minimum, span) -
                                DashboardChart.y(0, minimum, span),
                            ),
                          )}
                          rx="3"
                          fill={DashboardChart.color(point.value)}
                        >
                          <title>
                            {point.label}:{' '}
                            {DashboardPresentation.money(point.value)}
                          </title>
                        </rect>
                      ) : (
                        <circle
                          cx={this.x(index)}
                          cy={DashboardChart.y(point.value, minimum, span)}
                          r="4"
                          fill="var(--chart-series)"
                        >
                          <title>
                            {point.label}:{' '}
                            {DashboardPresentation.money(point.value)}
                          </title>
                        </circle>
                      ))}
                    <text
                      className={
                        index === 0 ||
                        index === points.length - 1 ||
                        index === Math.floor((points.length - 1) / 2)
                          ? undefined
                          : 'chart-intermediate-date'
                      }
                      x={this.x(index)}
                      y="246"
                      textAnchor="middle"
                      fill="var(--chart-ink)"
                    >
                      {point.label.startsWith('20')
                        ? point.label.slice(5)
                        : point.label}
                    </text>
                    {point.value === null && (
                      <text
                        x={this.x(index)}
                        y="193"
                        textAnchor="middle"
                        fill="var(--chart-ink)"
                      >
                        —
                      </text>
                    )}
                  </g>
                ))}
              </svg>
            </Box>
            <Box
              component="details"
              sx={{
                '& summary': {
                  minHeight: 44,
                  alignContent: 'center',
                  cursor: 'pointer',
                  fontSize: 14,
                },
              }}
            >
              <Box component="summary">Chart data</Box>
              <Box
                component="table"
                sx={{
                  width: '100%',
                  fontSize: 14,
                  textAlign: 'left',
                  borderCollapse: 'collapse',
                  '& th, & td': { px: 1.5, py: 1 },
                  '& th': {
                    bgcolor: 'primary.main',
                    color: 'primary.contrastText',
                  },
                  '& td': { borderBottom: '1px solid', borderColor: 'divider' },
                  '& :is(th, td):last-child': {
                    textAlign: 'right',
                    fontVariantNumeric: 'tabular-nums',
                  },
                }}
              >
                <thead>
                  <tr>
                    <th scope="col">Observation</th>
                    <th scope="col">THB</th>
                  </tr>
                </thead>
                <tbody>
                  {points.map((point) => (
                    <tr key={point.label}>
                      <td>{point.label}</td>
                      <td>{DashboardPresentation.money(point.value)}</td>
                    </tr>
                  ))}
                </tbody>
              </Box>
            </Box>
          </Box>
        )}
        <Box
          component="details"
          sx={{
            mt: 1,
            '& summary': {
              minHeight: 44,
              alignContent: 'center',
              cursor: 'pointer',
              fontSize: 14,
            },
          }}
        >
          <Box component="summary">Source &amp; scope</Box>
          <Typography
            variant="caption"
            color="text.secondary"
            component="p"
            sx={{ mb: 0 }}
          >
            {source}
          </Typography>
        </Box>
      </Paper>
    );
  }

  private x(index: number): number {
    return 100 + index * (488 / Math.max(1, this.props.points.length - 1));
  }

  private static y(value: number, minimum: number, span: number): number {
    return 190 - ((value - minimum) / span) * 150;
  }

  private static tick(value: number): string {
    return new Intl.NumberFormat('en-US', {
      notation: 'compact',
      maximumFractionDigits: 1,
    }).format(value);
  }

  private static color(value: number): string {
    if (value < 0) {
      return 'var(--chart-loss)';
    }

    if (value > 0) {
      return 'var(--chart-gain)';
    }

    return 'var(--chart-ink)';
  }
}
