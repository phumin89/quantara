import { Component, type ReactNode } from 'react';
import type { BriefMessageProps } from './BriefMessageProps';
import ReportMarkdown from './ReportMarkdown';
import IntradayReportView from './IntradayReportView';
import { TeamRole } from '../../contracts/TeamRole';

export default class BriefMessage extends Component<BriefMessageProps> {
  public override render(): ReactNode {
    if (this.props.role === TeamRole.Intraday) {
      return <IntradayReportView {...this.props} />;
    }

    return (
      <ReportMarkdown
        output={this.props.output}
        idPrefix={this.props.idPrefix ?? 'report-'}
      />
    );
  }
}
