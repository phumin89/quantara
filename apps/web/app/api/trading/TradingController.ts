import { NextRequest, NextResponse } from 'next/server';
import { z } from 'zod';
import { PortfolioControlProxy } from '../../../shared/api/PortfolioControlProxy';
import { tradingRulesSchema } from '../../../features/trading/contracts/TradingRules';

export class TradingController {
  private static readonly actions = {
    config: z.strictObject({ rules: tradingRulesSchema }),
    mode: z.strictObject({
      mode: z.enum(['paper', 'manualReview', 'stopped']),
    }),
    activate: z.strictObject({
      accountKey: z.string().min(1).max(128),
      rulesHash: z.string().min(1).max(128),
      expiresAt: z.iso
        .datetime({ offset: true })
        .refine(
          (date) => Date.parse(date) > Date.now(),
          'Mandate expiry must be in the future.',
        ),
      acknowledgement: z.literal('enable-auto-trading'),
    }),
    disable: z.strictObject({}),
    'emergency-stop': z.strictObject({}),
  };

  public static get(this: void, request: NextRequest): Promise<NextResponse> {
    return TradingController.proxy(request, '');
  }

  public static async action(
    this: void,
    request: NextRequest,
    context: { params: Promise<{ action: string }> },
  ): Promise<NextResponse> {
    const { action } = await context.params;
    if (!Object.hasOwn(TradingController.actions, action)) {
      return NextResponse.json(
        { error: 'Unknown trading action.' },
        { status: 404 },
      );
    }

    return TradingController.proxy(
      request,
      `/${action}`,
      TradingController.actions[
        action as keyof typeof TradingController.actions
      ],
    );
  }

  private static proxy(
    request: NextRequest,
    path: string,
    schema?: z.ZodType,
  ): Promise<NextResponse> {
    return PortfolioControlProxy.forward(
      request,
      `/api/trading${path}`,
      schema,
    );
  }
}
