export class CodexOutputSchema {
  static create(role, requiresAssessment) {
    return {
      type: 'object',
      properties: {
        output: { type: 'string', minLength: 1, maxLength: 16000 },
        proposal: {
          anyOf: [
            { type: 'null' },
            {
              type: 'object',
              properties: {
                symbol: { type: 'string', pattern: '^[A-Z0-9]{1,30}$' },
                side: { type: 'string', enum: ['buy', 'sell'] },
                amount: {
                  type: 'string',
                  pattern: '^(?:0|[1-9]\\d{0,11})(?:\\.\\d{1,18})?$',
                },
                limitPrice: {
                  type: 'string',
                  pattern: '^(?:0|[1-9]\\d{0,11})(?:\\.\\d{1,18})?$',
                },
                reason: { type: 'string', minLength: 1, maxLength: 240 },
                evidenceAt: { type: 'string' },
                entryPlan: {
                  anyOf: [
                    { type: 'null' },
                    {
                      type: 'object',
                      properties: {
                        targetPrice: {
                          type: 'string',
                          pattern: '^(?:0|[1-9]\\d{0,11})(?:\\.\\d{1,18})?$',
                        },
                        invalidationPrice: {
                          type: 'string',
                          pattern: '^(?:0|[1-9]\\d{0,11})(?:\\.\\d{1,18})?$',
                        },
                        reviewAfterMinutes: {
                          type: 'integer',
                          minimum: 1,
                          maximum: 1440,
                        },
                      },
                      required: [
                        'targetPrice',
                        'invalidationPrice',
                        'reviewAfterMinutes',
                      ],
                      additionalProperties: false,
                    },
                  ],
                },
              },
              required: [
                'symbol',
                'side',
                'amount',
                'limitPrice',
                'reason',
                'evidenceAt',
                'entryPlan',
              ],
              additionalProperties: false,
            },
          ],
        },
        lesson: CodexOutputSchema.lesson(role),
        researchSymbols: {
          type: 'array',
          items: { type: 'string', pattern: '^[A-Z0-9]{1,30}$' },
          maxItems: role === 'ceo' ? 3 : 0,
        },
        assessment: {
          anyOf: [
            ...(requiresAssessment ? [] : [{ type: 'null' }]),
            {
              type: 'object',
              properties: {
                schemaVersion: { const: 'research-assessment-v1' },
                accountKey: {
                  type: 'string',
                  minLength: 1,
                  maxLength: 128,
                },
                runId: { type: 'string', format: 'uuid' },
                policyHash: { type: ['string', 'null'], maxLength: 256 },
                asOf: { type: 'string', format: 'date-time' },
                expiresAt: { type: 'string', format: 'date-time' },
                evidenceIds: {
                  type: 'array',
                  items: { type: 'string', maxLength: 256 },
                  minItems: 1,
                  maxItems: 16,
                },
                positions: {
                  type: 'array',
                  maxItems: 64,
                  items: {
                    type: 'object',
                    properties: {
                      symbol: {
                        type: 'string',
                        pattern: '^[A-Z0-9]{1,30}$',
                      },
                      action: {
                        enum: ['Keep', 'Reduce', 'Exit', 'Review'],
                      },
                      thesis: {
                        type: 'string',
                        minLength: 1,
                        maxLength: 800,
                      },
                      reason: {
                        type: 'string',
                        minLength: 1,
                        maxLength: 800,
                      },
                      reviewAfter: { type: 'string', format: 'date-time' },
                      expiresAt: { type: 'string', format: 'date-time' },
                      missingFields: {
                        type: 'array',
                        items: { type: 'string', maxLength: 80 },
                        maxItems: 12,
                      },
                    },
                    required: [
                      'symbol',
                      'action',
                      'thesis',
                      'reason',
                      'reviewAfter',
                      'expiresAt',
                      'missingFields',
                    ],
                    additionalProperties: false,
                  },
                },
                opportunities: {
                  type: 'array',
                  maxItems: 16,
                  items: {
                    type: 'object',
                    properties: {
                      symbol: {
                        type: 'string',
                        pattern: '^[A-Z0-9]{1,30}$',
                      },
                      reason: {
                        type: 'string',
                        minLength: 1,
                        maxLength: 800,
                      },
                      reconsiderWhen: {
                        type: 'string',
                        minLength: 1,
                        maxLength: 800,
                      },
                      reviewAfter: { type: 'string', format: 'date-time' },
                    },
                    required: [
                      'symbol',
                      'reason',
                      'reconsiderWhen',
                      'reviewAfter',
                    ],
                    additionalProperties: false,
                  },
                },
              },
              required: [
                'schemaVersion',
                'accountKey',
                'runId',
                'policyHash',
                'asOf',
                'expiresAt',
                'evidenceIds',
                'positions',
                'opportunities',
              ],
              additionalProperties: false,
            },
          ],
        },
      },
      required: [
        'output',
        'proposal',
        'lesson',
        'researchSymbols',
        'assessment',
      ],
      additionalProperties: false,
    };
  }

  static lesson(role) {
    if (role === 'ceo' || role === 'analyst') return { type: 'null' };
    return {
      type: 'object',
      properties: {
        lesson: { type: 'string', minLength: 1, maxLength: 500 },
        evidence: { type: 'string', minLength: 1, maxLength: 500 },
        nextTest: { type: 'string', minLength: 1, maxLength: 500 },
      },
      required: ['lesson', 'evidence', 'nextTest'],
      additionalProperties: false,
    };
  }
}
