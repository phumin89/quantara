import { NextRequest, NextResponse } from 'next/server';
import { z } from 'zod';
import { PortfolioControlProxy } from '../../../../shared/api/PortfolioControlProxy';
import { riskPolicySchema } from '../../../../features/crypto/contracts/RiskPolicy';

export class RiskController {
  public static get(this: void, request: NextRequest): Promise<NextResponse> {
    return PortfolioControlProxy.forward(request, '/api/crypto/risk');
  }

  public static refresh(
    this: void,
    request: NextRequest,
  ): Promise<NextResponse> {
    return PortfolioControlProxy.forward(
      request,
      '/api/crypto/risk/refresh',
      z.strictObject({}),
      90000,
    );
  }

  public static configure(
    this: void,
    request: NextRequest,
  ): Promise<NextResponse> {
    return PortfolioControlProxy.forward(
      request,
      '/api/crypto/risk/policy',
      z.strictObject({
        policy: riskPolicySchema,
        expectedPolicyHash: z.string().regex(/^[a-fA-F0-9]{64}$/),
      }),
    );
  }
}
