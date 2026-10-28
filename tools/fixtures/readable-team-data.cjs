const base = require('./workspace-data.cjs');
const date = '2026-10-21';
const at = date + 'T06:00:00Z';
const plainBody =
  'adaptive-intraday-v2 | stopped\nตลาดจำลอง: BTC ยังยืนยันไม่ได้; TEST ข้อมูลขาด\nรอข้อมูลใหม่; ไม่ให้อำนาจส่งคำสั่ง\n\nอ้างอิง asOf 2026-10-21T05:55:00.1234567Z ข้อมูล 15m/60m ยังไม่ทราบ ต้องวัดอายุแยกแต่ละแหล่ง\n\nSource fetched: 05:54:39–40Z\n\n• BTC/THB: ราคาจำลอง 100.00 ต่ำกว่า EMA; ยังไม่ยืนยัน breakout\n• TEST/THB: ขาดแท่งต่อเนื่อง; สาเหตุ Unknown ห้ามเติม volume ศูนย์\n\nWallet จำลอง: ยอดศูนย์ แต่ engine lot ยังไม่ตรง โหมด stopped ไม่เปิด exit อัตโนมัติ; P&L บัญชี Unknown\n\nค่าธรรมเนียมและการ fill ยังไม่ทราบ ไม่ใช่หลักฐานกำไร\n\nINTRA-01 Pending: รอแท่งปิดและ quote/book สด; เวลาทบทวนไม่ใช่อำนาจเทรด';
const plain = plainBody + '\n\nPolicy hash: synthetic-fixture';
const markdown =
  '# Synthetic Markdown\n\nThis is recorded synthetic evidence, not an instruction to trade.\n\n## Reasons\n\n- Missing complete history.\n- Costs and fill remain unknown.\n\n| Market | Evidence |\n| --- | --- |\n| BTC | Unknown |\n\n![tracker](https://example.com/tracker.png)\n\n<script>window.syntheticReportScriptRan=true</script>\n\n[unsafe](javascript:alert(1))\n\n' +
  'LongSyntheticUnbrokenText'.repeat(170);
function runs(count, account) {
  return Array.from({ length: count }, (_, index) => {
    const run = {
      ...structuredClone(base.run),
      accountKey: 'synthetic-readable-' + account,
      id:
        '00000000-0000-4000-8000-' +
        String(index + 1 + (account === 'b' ? 100 : 0)).padStart(12, '0'),
      createdAt: date + 'T05:' + String(index).padStart(2, '0') + ':00Z',
      updatedAt: date + 'T05:' + String(index).padStart(2, '0') + ':01Z',
      status: index === 0 ? 'failed' : 'completed',
      error:
        index === 0
          ? 'Synthetic evidence review failed; assessment remains unknown.'
          : null,
    };
    if (index === 0 || index === 2) {
      run.steps = [
        {
          role: 'intraday',
          status: 'completed',
          output:
            index === 0
              ? plain
              : 'Unrecognized plain report.\n\nKeep all original caveats; data Unknown.\n\n' +
                'Synthetic long plain evidence. '.repeat(80),
          error: null,
          completedAt: run.updatedAt,
        },
        {
          role: 'reviewer',
          status: index === 0 ? 'failed' : 'completed',
          output:
            index === 0 ? null : 'Synthetic reviewer: uncertainty preserved.',
          error: index === 0 ? run.error : null,
          completedAt: run.updatedAt,
        },
      ];
    } else {
      run.steps = run.steps.map((step) => ({
        ...step,
        output:
          index === 1
            ? markdown
            : 'Synthetic new reply ' +
              String(index + 1) +
              ' from ' +
              step.role +
              '.',
        completedAt: run.updatedAt,
      }));
    }
    return run;
  });
}
module.exports = { base, date, at, plain, markdown, runs };
