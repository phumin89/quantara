import { PureComponent } from 'react';
import { Box, Chip, Stack, Typography } from '@mui/material';
import type { BriefMessageProps } from './BriefMessageProps';
import { IntradayReportLayout } from '../../presentation/IntradayReportLayout';
import ReportMarkdown from './ReportMarkdown';

export default class IntradayReportView extends PureComponent<BriefMessageProps> {
  public override render() {
    const report = IntradayReportLayout.parse(this.props.output);
    if (!report) {
      return (
        <ReportMarkdown
          output={this.props.output}
          idPrefix={this.props.idPrefix ?? 'report-'}
        />
      );
    }

    const mode = report.mode === 'stopped' ? 'Stopped' : report.mode;
    const prefix = this.props.idPrefix ?? 'report-';

    return (
      <Stack
        spacing={3}
        sx={{
          minWidth: 0,
          maxWidth: '75ch',
          overflowWrap: 'anywhere',
          '& summary': {
            minHeight: 44,
            alignContent: 'center',
            cursor: 'pointer',
            borderRadius: 'var(--radius-control)',
            '&:focus-visible': {
              outline: '2px solid',
              outlineColor: 'info.main',
              outlineOffset: 3,
            },
          },
        }}
      >
        <Box>
          <Stack
            direction="row"
            sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap', mb: 1.5 }}
          >
            <Chip
              size="small"
              label={`${mode} · recorded`}
              color={report.mode === 'stopped' ? 'warning' : 'default'}
              sx={{ borderRadius: 'var(--radius-tag)', fontWeight: 700 }}
            />
            <Typography
              variant="caption"
              color="text.secondary"
            >
              {report.policy} · Research only
            </Typography>
          </Stack>
          <Typography
            component="h4"
            sx={{ fontSize: 20, fontWeight: 800, mb: 1 }}
          >
            Recorded summary
          </Typography>
          <Box
            component="ul"
            sx={{
              m: 0,
              pl: 2.5,
              fontSize: 16,
              lineHeight: 1.65,
              '& li': { mb: 0.75 },
            }}
          >
            {report.summary.map((line, index) => (
              <li key={index}>{line}</li>
            ))}
          </Box>
          <Typography
            variant="caption"
            color="text.secondary"
            sx={{ display: 'block', mt: 1.5 }}
          >
            Reported reference:{' '}
            {report.referenceAt
              ? new Intl.DateTimeFormat('en-GB', {
                  timeZone: 'Asia/Bangkok',
                  dateStyle: 'medium',
                  timeStyle: 'short',
                }).format(new Date(report.referenceAt)) + ' Bangkok'
              : 'Not recorded'}
            . Saved research; source ages and current trading state may differ.
          </Typography>
        </Box>
        {report.sections.map((section, index) =>
          section.metadata ? (
            <Box
              component="details"
              key={index}
            >
              <Box
                component="summary"
                sx={{ fontWeight: 600 }}
              >
                {section.heading}
              </Box>
              <ReportMarkdown
                output={section.text}
                idPrefix={`${prefix}section-${String(index)}-`}
              />
            </Box>
          ) : (
            <Box
              component="section"
              key={index}
            >
              <Typography
                component="h5"
                sx={{ fontSize: 18, fontWeight: 800, mb: 1 }}
              >
                {section.heading}
              </Typography>
              <ReportMarkdown
                output={section.text.replace(/^\s*•\s+/gm, '- ')}
                idPrefix={`${prefix}section-${String(index)}-`}
              />
            </Box>
          ),
        )}
        <Box component="details">
          <Box
            component="summary"
            sx={{ fontWeight: 600 }}
          >
            Original report
          </Box>
          <Box
            component="pre"
            sx={{
              whiteSpace: 'pre-wrap',
              overflowWrap: 'anywhere',
              fontFamily: 'inherit',
              fontSize: 16,
              lineHeight: 1.65,
              m: 0,
              mt: 1,
            }}
          >
            {this.props.output}
          </Box>
        </Box>
      </Stack>
    );
  }
}
