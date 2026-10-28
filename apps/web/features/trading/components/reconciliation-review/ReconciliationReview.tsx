'use client';

import { Component, type ReactNode } from 'react';
import {
  Alert,
  Box,
  Button,
  Checkbox,
  FormControlLabel,
  Stack,
  Typography,
} from '@mui/material';
import type { ReconciliationReviewProps } from './ReconciliationReviewProps';
import TradingFormatting from '../../presentation/TradingFormatting';

export default class ReconciliationReview extends Component<ReconciliationReviewProps> {
  public override render(): ReactNode {
    const { preview, acknowledged, busy, blocked, expired, uncertain } =
      this.props;
    const result = preview.result;
    let confirmLabel =
      preview.kind === 'manual-sale'
        ? 'Confirm sold holdings'
        : 'Confirm cash balance';
    if (uncertain) {
      confirmLabel = 'Retry same confirmation';
    }

    if (busy) {
      confirmLabel = 'Please wait…';
    }

    return (
      <Stack
        spacing={2}
        data-testid="reconciliation-preview"
        sx={{ borderTop: '1px solid', borderColor: 'divider', pt: 2 }}
      >
        <Typography
          component="h3"
          sx={{
            fontFamily: 'var(--font-display)',
            fontSize: 22,
            fontWeight: 850,
          }}
        >
          {preview.kind === 'manual-sale'
            ? 'Review sold holdings'
            : 'Review current cash'}
        </Typography>
        <Typography sx={{ overflowWrap: 'anywhere' }}>
          Bitkub account …{preview.binding.accountKey.slice(-8)} · Broker
          observed {TradingFormatting.time(result.brokerObservedAt)}
        </Typography>
        {preview.kind === 'manual-sale' ? (
          <Box
            component="dl"
            sx={{
              m: 0,
              p: 2,
              bgcolor: 'var(--surface-soft)',
              borderRadius: 'var(--radius-control)',
              '& dt': { color: 'text.secondary', mt: 1 },
              '& dd': {
                m: 0,
                overflowWrap: 'anywhere',
                fontVariantNumeric: 'tabular-nums',
                fontWeight: 750,
              },
            }}
          >
            <dt>Previously recorded holding</dt>
            <dd>
              {preview.binding.originalLot?.quantity ?? 'Unknown'}{' '}
              {preview.result.symbol} · Original recorded cost{' '}
              {preview.binding.originalLot?.costThb ?? 'Unknown'} THB
            </dd>
            <dt>Verified sold quantity</dt>
            <dd>
              {preview.result.allocatedSoldQuantity} {preview.result.symbol}
            </dd>
            <dt>Unresolved historical difference</dt>
            <dd>
              {preview.result.unallocatedHistoricalQuantity}{' '}
              {preview.result.symbol}
            </dd>
            <dt>Allocated cost / realized net profit</dt>
            <dd>
              {preview.result.allocatedCostThb ?? 'Unknown'} /{' '}
              {preview.result.realizedNetProfitThb ?? 'Unknown'} THB
            </dd>
            <dt>Broker total / available now</dt>
            <dd>
              {preview.result.brokerBalance.quantity} /{' '}
              {preview.result.brokerBalance.available ?? 'Unknown'}{' '}
              {preview.result.symbol}
            </dd>
          </Box>
        ) : (
          <Box
            component="dl"
            sx={{
              m: 0,
              p: 2,
              bgcolor: 'var(--surface-soft)',
              borderRadius: 'var(--radius-control)',
              '& dt': { color: 'text.secondary', mt: 1 },
              '& dd': {
                m: 0,
                overflowWrap: 'anywhere',
                fontVariantNumeric: 'tabular-nums',
                fontWeight: 750,
              },
            }}
          >
            <dt>Previously recorded cash</dt>
            <dd>{preview.binding.previousCashThb} THB</dd>
            <dt>Verified broker total</dt>
            <dd>{preview.result.cashThb} THB</dd>
            <dt>Verified available</dt>
            <dd>{preview.result.availableThb} THB</dd>
          </Box>
        )}
        <details>
          <summary>Extended evidence and accounting history</summary>
          <Stack
            spacing={1}
            sx={{ pt: 1 }}
          >
            <Typography variant="body2">
              {preview.binding.riskSummary}
            </Typography>
            <Typography variant="body2">
              {uncertain
                ? 'Original history and unknown cost/profit remain recorded. Checking a saved confirmation does not change trading controls.'
                : 'Original history and unknown cost/profit remain recorded. Trading stays stopped and the mandate stays revoked.'}
            </Typography>
          </Stack>
        </details>
        {preview.kind === 'manual-sale' &&
          preview.result.cashSettlementPending && (
            <Alert severity="warning">
              Sale proceeds are not verified for credit. This step archives
              empty inventory only; review current cash separately.
            </Alert>
          )}
        {preview.kind === 'cash-baseline' &&
          preview.result.currentDayProfitUnknown && (
            <Alert severity="warning">
              Current-day profit is unknown. New buying remains blocked; this
              confirmation does not remove that guard.
            </Alert>
          )}
        <Typography variant="body2">{result.acknowledgement}</Typography>
        <Typography variant="body2">
          Review expires {TradingFormatting.time(result.expiresAt)}.{' '}
          {uncertain
            ? 'The server can recover an already saved confirmation after this time.'
            : 'The server rechecks the account and evidence before recording.'}
        </Typography>
        {expired && !uncertain && (
          <Alert severity="warning">
            This preview expired. Discard it and request a fresh preview.
          </Alert>
        )}
        {busy && uncertain && (
          <Alert severity="info">
            Waiting for the server to verify this review.
          </Alert>
        )}
        {uncertain && !busy && (
          <Alert severity="warning">
            The confirmation may already be saved. Retry only this same
            confirmation to check its result. Nothing is retried automatically.
          </Alert>
        )}
        {blocked && (
          <Alert severity="warning">
            {uncertain
              ? 'The current account could not be verified. Refresh status before retrying this saved confirmation.'
              : 'Current account, rules or stopped status could not be matched. Confirmation is disabled. Reload status; the saved review remains available to inspect or explicitly discard.'}
          </Alert>
        )}
        <FormControlLabel
          control={
            <Checkbox
              checked={acknowledged}
              disabled={busy || blocked || (expired && !uncertain)}
              onChange={(_event, checked) => {
                this.props.onAcknowledge(checked);
              }}
            />
          }
          label="I reviewed this exact account, evidence and accounting change."
        />
        <Stack
          direction={{ xs: 'column', sm: 'row' }}
          spacing={1}
        >
          <Button
            variant="contained"
            disabled={
              busy || blocked || !acknowledged || (expired && !uncertain)
            }
            onClick={this.props.onConfirm}
            sx={{ minHeight: 44, textTransform: 'none' }}
          >
            {confirmLabel}
          </Button>
          <Button
            disabled={busy}
            onClick={this.props.onDiscard}
            sx={{ minHeight: 44, textTransform: 'none' }}
          >
            {uncertain ? 'Discard saved confirmation' : 'Discard preview'}
          </Button>
        </Stack>
      </Stack>
    );
  }
}
