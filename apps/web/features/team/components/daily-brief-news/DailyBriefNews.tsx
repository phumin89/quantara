import { Component, type ReactNode } from 'react';
import { Box, Paper, Stack, Typography } from '@mui/material';
import type { NewsPacket } from '../../contracts/NewsPacket';
import type { DailyBriefNewsProps } from './DailyBriefNewsProps';

export default class DailyBriefNews extends Component<DailyBriefNewsProps> {
  public override render(): ReactNode {
    const { news, error } = this.props;

    return (
      <Paper
        variant="outlined"
        sx={{ borderRadius: '8px', p: { xs: 2, sm: 3 } }}
      >
        <Box component="details">
          <Box
            component="summary"
            sx={{
              cursor: 'pointer',
              minHeight: 44,
              alignContent: 'center',
              fontWeight: 700,
            }}
          >
            Market news
            {news ? ` · ${String(news.events.length)} relevant updates` : ''}
          </Box>
          {this.renderNews(news, error)}
          {news && news.omittedEventCount > 0 && (
            <Typography
              variant="caption"
              color="text.secondary"
              sx={{ display: 'block', mt: 1 }}
            >
              {news.omittedEventCount} additional events omitted for compact
              display.
            </Typography>
          )}
        </Box>
      </Paper>
    );
  }

  private renderNews(news: NewsPacket | null, error: string | null): ReactNode {
    if (error) {
      return (
        <Typography color="text.secondary">
          News unavailable · {error}
        </Typography>
      );
    }

    if (!news) {
      return <Typography color="text.secondary">News unavailable.</Typography>;
    }

    return (
      <Stack
        spacing={1}
        sx={{ mt: 1 }}
      >
        {news.events.length === 0 && (
          <Typography color="text.secondary">
            No relevant events available. Check source coverage below.
          </Typography>
        )}
        {news.events.map((event) => (
          <Box key={`${event.eventId}-${String(event.revision)}`}>
            <Typography
              variant="body2"
              sx={{ fontWeight: 600 }}
            >
              <Box
                component="a"
                href={event.url}
                target="_blank"
                rel="noreferrer noopener"
                sx={{ color: 'inherit' }}
              >
                {event.title}
              </Box>
            </Typography>
            <Typography
              variant="caption"
              color="text.secondary"
            >
              {event.symbols.length > 0
                ? event.symbols.join(', ')
                : 'Market-wide'}{' '}
              · {event.factStatus}
              {event.revision > 1
                ? ` · Revision ${String(event.revision)}`
                : ''}
              {' · '}
              {event.publishedAt
                ? `Published ${DailyBriefNews.formatBangkok(event.publishedAt)}`
                : `Published time unknown · First seen ${DailyBriefNews.formatBangkok(event.firstSeenAt)}`}
            </Typography>
          </Box>
        ))}
        {news.sourceStates.map((source) => (
          <Typography
            key={source.id}
            variant="caption"
            color="text.secondary"
          >
            {source.label}: {source.status}
            {source.reason ? ` · ${source.reason}` : ''}
            {source.lastSuccessAt
              ? ` · Last success ${DailyBriefNews.formatBangkok(source.lastSuccessAt)}`
              : ' · Last success unknown'}
            {source.nextRetryAt
              ? ` · Retry ${DailyBriefNews.formatBangkok(source.nextRetryAt)}`
              : ''}
          </Typography>
        ))}
        <Typography
          variant="caption"
          color="text.secondary"
        >
          Pilot:{' '}
          {news.pilot.completed72Hours
            ? '72-hour validation complete'
            : '72-hour validation collecting'}
          {news.pilot.startedAt
            ? ` · Started ${DailyBriefNews.formatBangkok(news.pilot.startedAt)}`
            : ''}
          {news.pilot.lastPollAt
            ? ` · Last poll ${DailyBriefNews.formatBangkok(news.pilot.lastPollAt)}`
            : ''}
          {` · Gaps ${String(news.pilot.gapCount)}`}
        </Typography>
      </Stack>
    );
  }

  private static formatBangkok(value: string): string {
    return new Date(value).toLocaleString('en-GB', {
      timeZone: 'Asia/Bangkok',
    });
  }
}
