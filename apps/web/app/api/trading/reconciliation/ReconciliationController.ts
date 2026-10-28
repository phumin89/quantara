import { NextRequest, NextResponse } from 'next/server';
import { z } from 'zod';
import { PortfolioControlProxy } from '../../../../shared/api/PortfolioControlProxy';
import { manualSaleReviewSchema } from '../../../../features/trading/contracts/ManualSaleReview';
import { cashBaselineReviewSchema } from '../../../../features/trading/contracts/CashBaselineReview';

export class ReconciliationController {
  private static readonly confirmation = z.strictObject({
    proof: z.string().min(1).max(6000),
    acknowledgement: z.string().min(1).max(400),
  });

  private static readonly actions = {
    'manual-sale/preview': z.strictObject({
      symbol: z.string().regex(/^[A-Z0-9]{1,30}$/),
      executionId: z
        .string()
        .min(1)
        .max(128)
        .refine((value) => value.trim() === value),
    }),
    'manual-sale/apply': ReconciliationController.confirmation,
    'manual-sale/cash-baseline/preview': z.strictObject({}),
    'manual-sale/cash-baseline/apply': ReconciliationController.confirmation,
  };

  public static async action(
    this: void,
    request: NextRequest,
    context: { params: Promise<{ path: string[] }> },
  ): Promise<NextResponse> {
    const { path } = await context.params;
    const action = path.join('/');
    if (!Object.hasOwn(ReconciliationController.actions, action)) {
      return NextResponse.json(
        { error: 'Unknown reconciliation action.' },
        { status: 404 },
      );
    }

    const response = await PortfolioControlProxy.forward(
      request,
      `/api/trading/reconciliation/${action}`,
      ReconciliationController.actions[
        action as keyof typeof ReconciliationController.actions
      ],
    );
    if (!response.ok) {
      return response;
    }

    try {
      // Preserve the server's decimal tokens before JavaScript can round them.
      const body: unknown = JSON.parse(
        await response.text(),
        ReconciliationController.exactNumber,
      );
      const schema = action.includes('/cash-baseline/')
        ? cashBaselineReviewSchema
        : manualSaleReviewSchema;
      const parsed = schema.safeParse(body);
      if (!parsed.success) {
        throw new Error('Invalid review contract.');
      }

      return NextResponse.json(parsed.data, {
        headers: { 'Cache-Control': 'no-store' },
      });
    } catch {
      return NextResponse.json(
        {
          error:
            'Reconciliation response could not be verified. A confirmation may already be recorded; do not automatically retry.',
        },
        { status: 502 },
      );
    }
  }

  private static exactNumber(
    this: void,
    _key: string,
    value: unknown,
    context?: { source?: string },
  ): unknown {
    if (typeof value !== 'number') {
      return value;
    }

    if (!context?.source) {
      throw new Error('Exact numeric JSON tokens are unavailable.');
    }

    return context.source;
  }
}
