export class WorkerFailure {
  static codes = Object.freeze({
    INPUT_INVALID: 'worker_input_invalid',
    INPUT_TOO_LARGE: 'worker_input_too_large',
    AUTH_UNAVAILABLE: 'worker_auth_unavailable',
    AUTHENTICATION_FAILED: 'worker_authentication_failed',
    RATE_LIMITED: 'worker_rate_limited',
    NETWORK_FAILED: 'worker_network_failed',
    TIMED_OUT: 'worker_timed_out',
    RESPONSE_INVALID: 'worker_response_invalid',
    PROPOSAL_INVALID: 'worker_proposal_invalid',
    LESSON_INVALID: 'worker_lesson_invalid',
    SELECTION_INVALID: 'worker_selection_invalid',
    RESPONSE_REJECTED: 'worker_response_rejected',
    UNSUPPORTED_TOOL: 'worker_unsupported_tool',
    RUNTIME_FAILED: 'worker_runtime_failed',
  });

  static messages = Object.freeze({
    worker_input_invalid: 'The worker request is invalid.',
    worker_input_too_large: 'The worker request is too large.',
    worker_auth_unavailable: 'Worker authentication is not configured.',
    worker_authentication_failed: 'Worker authentication failed.',
    worker_rate_limited: 'The worker service is rate limited.',
    worker_network_failed: 'The worker service could not be reached.',
    worker_timed_out: 'The worker timed out.',
    worker_response_invalid: 'The worker returned an invalid response.',
    worker_proposal_invalid: 'The worker returned an invalid pending proposal.',
    worker_lesson_invalid: 'The worker returned an invalid learning lesson.',
    worker_selection_invalid:
      'The worker returned an invalid research selection.',
    worker_response_rejected: 'The model declined the research request.',
    worker_unsupported_tool: 'The worker attempted an unsupported tool.',
    worker_runtime_failed: 'The worker failed unexpectedly.',
  });

  static classify(error) {
    const message =
      error instanceof Error ? error.message : String(error ?? '');
    const name = error instanceof Error ? error.name : '';
    const lower = `${name} ${message}`.toLowerCase();
    if (/input is too large|request is too large/.test(lower))
      return this.codes.INPUT_TOO_LARGE;
    if (/misalignment|refus/.test(lower)) return this.codes.RESPONSE_REJECTED;
    if (/invalid pending proposal/.test(lower))
      return this.codes.PROPOSAL_INVALID;
    if (/invalid learning lesson/.test(lower)) return this.codes.LESSON_INVALID;
    if (/invalid response research selection/.test(lower))
      return this.codes.SELECTION_INVALID;
    if (
      /invalid (?:json )?(?:report|response|pending proposal|learning lesson)|schema/.test(
        lower,
      )
    )
      return this.codes.RESPONSE_INVALID;
    if (
      /invalid worker input|invalid role handoff|unexpected token|json parse/.test(
        lower,
      )
    )
      return this.codes.INPUT_INVALID;
    if (
      /configure codex authentication|configure the isolated codex launcher|auth(entication)? unavailable|not configured/.test(
        lower,
      )
    )
      return this.codes.AUTH_UNAVAILABLE;
    if (
      /401|unauthori[sz]ed|authentication failed|invalid api key|login|refresh_token|invalid_grant|token.*expired/.test(
        lower,
      )
    )
      return this.codes.AUTHENTICATION_FAILED;
    if (/429|rate limit|rate_limit|quota|usage[_ ]limit/.test(lower))
      return this.codes.RATE_LIMITED;
    if (/abort|timeout|timed out|deadline/.test(lower))
      return this.codes.TIMED_OUT;
    if (/unsupported tool/.test(lower)) return this.codes.UNSUPPORTED_TOOL;
    if (/network|fetch|econn|socket|connection|stream disconnected/.test(lower))
      return this.codes.NETWORK_FAILED;
    return this.codes.RUNTIME_FAILED;
  }

  static from(error) {
    const code = this.classify(error);
    return Object.freeze({ code, message: this.messages[code] });
  }
}
