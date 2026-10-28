import { readFileSync } from 'node:fs';

export class CodexModelPolicy {
  static version = 'role-model-v1';

  // Capability snapshot: installed Codex catalog 2026-10-21. Availability still
  // depends on the authenticated runtime; unsupported requests fail closed.
  static models = Object.freeze(['gpt-6-luna', 'gpt-6.1-sol']);

  static roles = Object.freeze(
    Object.fromEntries(
      Object.entries(
        JSON.parse(
          readFileSync(
            new URL('./CodexModelPolicy.json', import.meta.url),
            'utf8',
          ),
        ),
      ).map(([role, value]) => [role, Object.freeze(value)]),
    ),
  );

  static validProfile(role, value) {
    return (
      value &&
      typeof value === 'object' &&
      !Array.isArray(value) &&
      Object.keys(value).sort().join(',') === 'effort,model' &&
      this.models.includes(value.model) &&
      ['low', 'medium', 'high'].includes(value.effort) &&
      (!['reviewer', 'quant', 'positions'].includes(role) ||
        (value.model === 'gpt-6.1-sol' && value.effort !== 'low'))
    );
  }

  static select(role, source = process.env) {
    if (!Object.hasOwn(this.roles, role))
      throw new Error('Invalid model policy.');
    if (source.QUANTARA_CODEX_PINNED_MODEL !== undefined) {
      let pinned;
      try {
        pinned = JSON.parse(source.QUANTARA_CODEX_PINNED_MODEL);
      } catch {
        throw new Error('Invalid model policy.');
      }
      if (
        !pinned ||
        Object.keys(pinned).sort().join(',') !==
          'actualModel,actualModelSource,fallback,policyVersion,provider,reasoningEffort,requestedModel,selectionSource' ||
        pinned.policyVersion !== this.version ||
        pinned.provider !== 'openai' ||
        pinned.actualModel !== null ||
        pinned.actualModelSource !== 'sdk-not-exposed' ||
        pinned.fallback !== null ||
        !['default', 'role-override'].includes(pinned.selectionSource) ||
        !this.validProfile(role, {
          model: pinned.requestedModel,
          effort: pinned.reasoningEffort,
        }) ||
        (pinned.selectionSource === 'default' &&
          (pinned.requestedModel !== this.roles[role].model ||
            pinned.reasoningEffort !== this.roles[role].effort))
      )
        throw new Error('Invalid model policy.');
      return Object.freeze(pinned);
    }
    let overrides = {};
    const configured = source.QUANTARA_CODEX_ROLE_POLICY;
    if (configured !== undefined) {
      if (typeof configured !== 'string' || configured.length > 2000)
        throw new Error('Invalid model policy.');
      try {
        overrides = JSON.parse(configured);
      } catch {
        throw new Error('Invalid model policy.');
      }
      if (
        !overrides ||
        typeof overrides !== 'object' ||
        Array.isArray(overrides)
      )
        throw new Error('Invalid model policy.');
      for (const [key, value] of Object.entries(overrides)) {
        if (!Object.hasOwn(this.roles, key) || !this.validProfile(key, value))
          throw new Error('Invalid model policy.');
      }
    }
    const chosen = Object.hasOwn(overrides, role)
      ? overrides[role]
      : this.roles[role];
    if (!this.validProfile(role, chosen))
      throw new Error('Invalid model policy.');
    return Object.freeze({
      policyVersion: this.version,
      requestedModel: chosen.model,
      provider: 'openai',
      reasoningEffort: chosen.effort,
      selectionSource: Object.hasOwn(overrides, role)
        ? 'role-override'
        : 'default',
      actualModel: null,
      actualModelSource: 'sdk-not-exposed',
      fallback: null,
    });
  }

  static usage(turn) {
    if (turn.usage == null) return null;
    const usage = {
      inputTokens: turn.usage.input_tokens,
      outputTokens: turn.usage.output_tokens,
      cachedInputTokens: turn.usage.cached_input_tokens,
    };
    if (
      Object.values(usage).some(
        (value) => !Number.isSafeInteger(value) || value < 0,
      ) ||
      usage.cachedInputTokens > usage.inputTokens
    )
      throw new Error('The worker returned invalid usage metadata.');
    return Object.freeze(usage);
  }
}
