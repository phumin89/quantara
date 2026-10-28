export class WorkerResponseDiagnostics {
  static stages = Object.freeze([
    'response_json',
    'response_shape',
    'report_content',
    'proposal_content',
    'lesson_content',
    'research_selection',
    'assessment_shape',
  ]);

  static create(stage, responseCharacters) {
    if (!this.stages.includes(stage)) return null;
    return Object.freeze({
      version: 1,
      stage,
      responseCharacters:
        Number.isInteger(responseCharacters) &&
        responseCharacters >= 0 &&
        responseCharacters <= 100000
          ? responseCharacters
          : null,
    });
  }
}
