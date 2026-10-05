'use client';

export function LiveOffice() {
  return <section className="live-office" aria-label="สำนักงาน Pixel ของ Quantara">
    <div className="live-office-heading"><h2>สำนักงาน Pixel</h2>
      <p>มุมมองสำนักงาน · ยังไม่แสดงกิจกรรมทีม Live</p></div>
    <iframe className="live-office-frame" title="สำนักงาน Pixel ของ Quantara" src="/office/live.html" sandbox="allow-scripts allow-same-origin" />
  </section>;
}
