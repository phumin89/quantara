export class IntradayReportLayout {
  public static parse(output: string): {
    policy: string;
    mode: string;
    summary: string[];
    referenceAt: string | null;
    sections: { heading: string; text: string; metadata: boolean }[];
  } | null {
    const paragraphs = output.trim().split(/\r?\n[\t ]*\r?\n/);
    const lines = paragraphs[0]?.split(/\r?\n/) ?? [];
    const header =
      /^(?:กลยุทธ์\s+)?(adaptive-intraday-v[123])\s*\|\s*(?:โหมด\s+)?(stopped|paper|manualReview|autoLive)\s*$/.exec(
        lines[0] ?? '',
      );
    if (
      !header ||
      lines.length < 2 ||
      lines.length > 8 ||
      /^(?:#{1,6}\s|```|\||>|[-*]\s)/m.test(output)
    ) {
      return null;
    }

    const reference =
      /(?:asOf|อ้างอิงข้อมูลถึง)\s+(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2}))/.exec(
        output,
      )?.[1];

    return {
      policy: header[1] ?? '',
      mode: header[2] ?? '',
      summary: lines.slice(1).filter((line) => line.trim() !== ''),
      referenceAt:
        reference && Number.isFinite(Date.parse(reference)) ? reference : null,
      sections: paragraphs
        .slice(1)
        .flatMap((paragraph) =>
          paragraph.split(
            /\r?\n(?=\s*•?\s*[A-Z0-9]{2,12}(?:\/[A-Z0-9]{2,12})*:\s)/,
          ),
        )
        .filter((text) => text.trim() !== '')
        .map((text) => {
          const market =
            /^\s*•?\s*([A-Z0-9]{2,12}(?:\/[A-Z0-9]{2,12})*):\s/.exec(text);
          const metadata =
            /^(?:Provenance:|Policy hash:|Run ID:)/i.test(text) &&
            !/unknown|unavailable|missing|gap|error|fail|stopped|stale|expired|delayed|outdated|limited|incomplete|uncertain|\bno\b|\bnot\b|ขาด|ไม่|ห้าม|เก่า|หมดอายุ|coverage|scan|monitored|series/i.test(
              text,
            );
          let heading = 'Recorded evidence';
          if (market) {
            heading = market[1] ?? heading;
          } else if (metadata) {
            heading = 'Source metadata';
          } else if (/^Source fetched:/i.test(text)) {
            heading = 'Source freshness';
          } else if (/asOf|อ้างอิงข้อมูลถึง/.test(text)) {
            heading = 'Data reference and coverage';
          } else if (/Wallet|ทุน|equity/i.test(text)) {
            heading = 'Wallet and accounting';
          } else if (/INTRA-\d|Pending|Blocked/.test(text)) {
            heading = 'Next review and blockers';
          } else if (/ค่าธรรมเนียม|fee/i.test(text)) {
            heading = 'Costs and uncertainty';
          } else if (/spread|depth|สแกน|scan/i.test(text)) {
            heading = 'Market quality and liquidity';
          }

          return {
            heading,
            text: market ? text.slice(market[0].length) : text,
            metadata,
          };
        }),
    };
  }
}
