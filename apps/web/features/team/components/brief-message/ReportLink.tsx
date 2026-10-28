import { PureComponent, type ComponentProps } from 'react';

export default class ReportLink extends PureComponent<ComponentProps<'a'>> {
  public override render() {
    const { href, title, children } = this.props;

    return href ? (
      <a
        id={this.props.id}
        aria-label={this.props['aria-label']}
        aria-describedby={this.props['aria-describedby']}
        href={href}
        title={title}
        target={href.startsWith('#') ? undefined : '_blank'}
        rel="noopener noreferrer"
      >
        {children}
      </a>
    ) : (
      <span>{children}</span>
    );
  }
}
