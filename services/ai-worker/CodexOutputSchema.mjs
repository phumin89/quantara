export class CodexOutputSchema {
  static create(role, requiresAssessment, context = null) {
    const schema = {
      type: 'object',
      properties: {
        output: {
          type: 'string',
          minLength: 1,
          maxLength: 16000,
          pattern: '\\S',
        },
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
                reason: {
                  type: 'string',
                  minLength: 1,
                  maxLength: 240,
                  pattern: '\\S',
                },
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
          maxItems: ['ceo', 'intraday'].includes(role) ? 3 : 0,
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
                        pattern: '\\S',
                      },
                      reason: {
                        type: 'string',
                        minLength: 1,
                        maxLength: 800,
                        pattern: '\\S',
                      },
                      reviewAfter: { type: 'string', format: 'date-time' },
                      expiresAt: { type: 'string', format: 'date-time' },
                      missingFields: {
                        type: 'array',
                        items: {
                          type: 'string',
                          minLength: 1,
                          maxLength: 80,
                          pattern: '\\S',
                        },
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
                        pattern: '\\S',
                      },
                      reconsiderWhen: {
                        type: 'string',
                        minLength: 1,
                        maxLength: 800,
                        pattern: '\\S',
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
    if (role !== 'reviewer') {
      schema.properties.proposal = { type: 'null' };
      schema.properties.assessment = { type: 'null' };
    } else if (requiresAssessment && context !== null) {
      if (
        typeof context !== 'object' ||
        Array.isArray(context) ||
        context.schemaVersion !== 'research-assessment-v1' ||
        !['accountKey', 'runId', 'asOf'].every(
          (field) =>
            typeof context[field] === 'string' && context[field].length > 0,
        ) ||
        !(context.policyHash === null || typeof context.policyHash === 'string')
      )
        throw new Error('Invalid worker input assessment context.');
      const assessment = schema.properties.assessment.anyOf.find(
        (item) => item.type === 'object',
      );
      for (const field of ['accountKey', 'runId', 'policyHash', 'asOf']) {
        assessment.properties[field].const = context[field];
      }
      if (Object.prototype.hasOwnProperty.call(context, 'evidenceIds')) {
        const ids = context.evidenceIds;
        const validIds =
          Array.isArray(ids) &&
          ids.length >= 1 &&
          ids.length <= 16 &&
          [...ids].every(
            (id) =>
              typeof id === 'string' &&
              id.trim().length > 0 &&
              id.length <= 256,
          ) &&
          new Set(ids).size === ids.length;
        if (!validIds)
          throw new Error('Invalid worker input assessment context.');
        assessment.properties.evidenceIds.items.enum = [...ids];
        assessment.properties.evidenceIds.maxItems = ids.length;
      }
      if (
        Object.prototype.hasOwnProperty.call(context, 'requiredPositionSymbols')
      ) {
        const symbols = context.requiredPositionSymbols;
        const validSymbols =
          Array.isArray(symbols) &&
          symbols.length <= 64 &&
          symbols.every(
            (symbol) =>
              typeof symbol === 'string' &&
              symbol !== 'THB' &&
              /^[A-Z0-9]{1,30}$/.test(symbol),
          ) &&
          new Set(symbols).size === symbols.length;
        if (!validSymbols)
          throw new Error('Invalid worker input assessment context.');
        assessment.properties.positions.minItems = symbols.length;
        assessment.properties.positions.maxItems = symbols.length;
        if (symbols.length > 0) {
          assessment.properties.positions.items.properties.symbol.enum = [
            ...symbols,
          ];
        }
      }
      const hasEarliestReviewAt = Object.prototype.hasOwnProperty.call(
        context,
        'earliestReviewAt',
      );
      const hasExpiresAt = Object.prototype.hasOwnProperty.call(
        context,
        'expiresAt',
      );
      if (hasEarliestReviewAt !== hasExpiresAt) {
        throw new Error('Invalid worker input assessment context.');
      }
      if (hasEarliestReviewAt && hasExpiresAt) {
        const isoDateTime =
          /^(?:\d{4})-(?:\d{2})-(?:\d{2})T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/;
        const asOfTime = Date.parse(context.asOf);
        const earliestReviewTime = Date.parse(context.earliestReviewAt);
        const expiresTime = Date.parse(context.expiresAt);
        if (
          typeof context.earliestReviewAt !== 'string' ||
          typeof context.expiresAt !== 'string' ||
          !isoDateTime.test(context.earliestReviewAt) ||
          !isoDateTime.test(context.expiresAt) ||
          !Number.isFinite(asOfTime) ||
          !Number.isFinite(earliestReviewTime) ||
          !Number.isFinite(expiresTime) ||
          !(asOfTime < earliestReviewTime && earliestReviewTime < expiresTime)
        ) {
          throw new Error('Invalid worker input assessment context.');
        }
        assessment.properties.expiresAt.const = context.expiresAt;
        assessment.properties.positions.items.properties.expiresAt.const =
          context.expiresAt;
        assessment.properties.positions.items.properties.reviewAfter.const =
          context.earliestReviewAt;
        assessment.properties.opportunities.items.properties.reviewAfter.const =
          context.earliestReviewAt;
      }
      if (
        Object.prototype.hasOwnProperty.call(
          context,
          'allowedOpportunitySymbols',
        )
      ) {
        const symbols = context.allowedOpportunitySymbols;
        const validSymbols =
          Array.isArray(symbols) &&
          symbols.length <= 1000 &&
          symbols.every(
            (symbol) =>
              typeof symbol === 'string' &&
              symbol !== 'THB' &&
              /^[A-Z0-9]{1,30}$/.test(symbol),
          ) &&
          new Set(symbols).size === symbols.length;
        if (!validSymbols)
          throw new Error('Invalid worker input assessment context.');
        assessment.properties.opportunities.maxItems = Math.min(
          16,
          symbols.length,
        );
        if (symbols.length > 0) {
          assessment.properties.opportunities.items.properties.symbol.enum = [
            ...symbols,
          ];
        }
      }
    }
    if (context?.aiPolicy?.enabled === true) {
      schema.properties.policyCandidate = { type: 'null' };
      schema.required.push('policyCandidate');
      if (role !== 'reviewer') {
        schema.properties.policyCandidate = { type: 'null' };
      } else {
        schema.properties.policyCandidate = {
          anyOf: [
            { type: 'null' },
            {
              type: 'object',
              properties: {
                runId: { type: 'string', format: 'uuid' },
                accountKey: { type: 'string', minLength: 1, maxLength: 128 },
                baselineRulesHash: {
                  type: 'string',
                  pattern: '^[a-f0-9]{64}$',
                },
                asOf: { type: 'string', format: 'date-time' },
                expiresAt: { type: 'string', format: 'date-time' },
                evidenceIds: {
                  type: 'array',
                  minItems: 1,
                  maxItems: 16,
                  items: { type: 'string', minLength: 1, maxLength: 256 },
                },
                reason: {
                  type: 'string',
                  minLength: 1,
                  maxLength: 800,
                  pattern: '\\S',
                },
                parameters: {
                  type: 'object',
                  properties: {
                    maxPositions: { type: 'integer', minimum: 1, maximum: 16 },
                    maxSpreadBps: { type: 'number', minimum: 0, maximum: 1000 },
                    slippageBps: { type: 'number', minimum: 0, maximum: 1000 },
                    takeProfitBps: {
                      type: 'number',
                      exclusiveMinimum: 0,
                      maximum: 10000,
                    },
                    stopLossBps: {
                      type: 'number',
                      exclusiveMinimum: 0,
                      maximum: 10000,
                    },
                    trailingStopBps: {
                      type: 'number',
                      exclusiveMinimum: 0,
                      maximum: 10000,
                    },
                    volumeRatio: { type: 'number', minimum: 1, maximum: 10 },
                    timeExitMinutes: {
                      type: 'integer',
                      minimum: 1,
                      maximum: 1440,
                    },
                    cooldownMinutes: {
                      type: 'integer',
                      minimum: 0,
                      maximum: 1440,
                    },
                    orderLifetimeMinutes: {
                      type: 'integer',
                      minimum: 1,
                      maximum: 1440,
                    },
                    maximumExposureFraction: {
                      type: 'number',
                      exclusiveMinimum: 0,
                      maximum: 1,
                    },
                    maximumConcentrationFraction: {
                      type: 'number',
                      exclusiveMinimum: 0,
                      maximum: 1,
                    },
                    riskToStopFraction: {
                      type: 'number',
                      exclusiveMinimum: 0,
                      maximum: 1,
                    },
                    dailyLossFraction: {
                      type: 'number',
                      exclusiveMinimum: 0,
                      maximum: 1,
                    },
                    atrMultiplier: { type: 'number', minimum: 1, maximum: 10 },
                  },
                  required: [
                    'maxPositions',
                    'maxSpreadBps',
                    'slippageBps',
                    'takeProfitBps',
                    'stopLossBps',
                    'trailingStopBps',
                    'volumeRatio',
                    'timeExitMinutes',
                    'cooldownMinutes',
                    'orderLifetimeMinutes',
                    'maximumExposureFraction',
                    'maximumConcentrationFraction',
                    'riskToStopFraction',
                    'dailyLossFraction',
                    'atrMultiplier',
                  ],
                  additionalProperties: false,
                },
              },
              required: [
                'runId',
                'accountKey',
                'baselineRulesHash',
                'asOf',
                'expiresAt',
                'evidenceIds',
                'reason',
                'parameters',
              ],
              additionalProperties: false,
            },
          ],
        };
      }
    }
    return schema;
  }

  static lesson(role) {
    if (role !== 'reviewer') return { type: 'null' };
    return {
      type: 'object',
      properties: {
        lesson: {
          type: 'string',
          minLength: 1,
          maxLength: 500,
          pattern: '^(?:\\S|\\S[\\s\\S]*\\S)$',
        },
        evidence: {
          type: 'string',
          minLength: 1,
          maxLength: 500,
          pattern: '^(?:\\S|\\S[\\s\\S]*\\S)$',
        },
        nextTest: {
          type: 'string',
          minLength: 1,
          maxLength: 500,
          pattern: '^(?:\\S|\\S[\\s\\S]*\\S)$',
        },
      },
      required: ['lesson', 'evidence', 'nextTest'],
      additionalProperties: false,
    };
  }
}
