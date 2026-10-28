import { PureComponent } from 'react';
import { Box } from '@mui/material';
import Markdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import type { Root, RootContent } from 'hast';
import type { ReportMarkdownProps } from './ReportMarkdownProps';
import ReportTable from './ReportTable';
import ReportLink from './ReportLink';

export default class ReportMarkdown extends PureComponent<ReportMarkdownProps> {
  public override render() {
    return (
      <Box
        sx={{
          maxWidth: '75ch',
          minWidth: 0,
          fontSize: 16,
          lineHeight: 1.65,
          overflowWrap: 'anywhere',
          '& p': { mt: 0, mb: 1.5, whiteSpace: 'pre-line' },
          '& h4, & h5, & h6': {
            mt: 3,
            mb: 1,
            lineHeight: 1.35,
            fontWeight: 800,
          },
          '& h4': { fontSize: 20 },
          '& h5': { fontSize: 18 },
          '& h6': { fontSize: 16 },
          '& ul, & ol': { pl: 3, my: 1.5 },
          '& li': { mb: 0.75 },
          '& a': {
            color: 'info.main',
            textUnderlineOffset: 3,
            '&:focus-visible': {
              outline: '2px solid',
              outlineColor: 'info.main',
              outlineOffset: 3,
              borderRadius: 'var(--radius-tag)',
            },
          },
          '& pre': {
            maxWidth: '100%',
            overflowX: 'auto',
            bgcolor: 'var(--surface-soft)',
            p: 1.5,
            borderRadius: 'var(--radius-control)',
            fontSize: 14,
          },
          '& code': { fontSize: '0.875em' },
          '& blockquote': {
            ml: 0,
            px: 2,
            py: 1,
            bgcolor: 'var(--surface-soft)',
            borderRadius: 'var(--radius-control)',
            '& p:last-child': { mb: 0 },
          },
        }}
      >
        <Markdown
          skipHtml
          disallowedElements={['img']}
          remarkPlugins={[remarkGfm]}
          rehypePlugins={[this.scopeFootnotes]}
          remarkRehypeOptions={{
            clobberPrefix: this.props.idPrefix ?? 'report-',
          }}
          urlTransform={ReportMarkdown.safeUrl}
          components={{
            table: ReportTable,
            a: ReportLink,
            h1: 'h4',
            h2: 'h5',
            h3: 'h6',
          }}
        >
          {this.props.output}
        </Markdown>
      </Box>
    );
  }

  private readonly scopeFootnotes =
    () =>
    (tree: Root): void => {
      const label = (this.props.idPrefix ?? 'report-') + 'footnote-label';
      this.visitFootnotes(tree, label);
    };

  private visitFootnotes(node: Root | RootContent, label: string): void {
    if (node.type === 'element') {
      if (node.properties.id === 'footnote-label') {
        node.properties.id = label;
      }

      if (Array.isArray(node.properties.ariaDescribedBy)) {
        node.properties.ariaDescribedBy = node.properties.ariaDescribedBy.map(
          (value) => (value === 'footnote-label' ? label : value),
        );
      }
    }

    if ('children' in node) {
      node.children.forEach((child) => {
        this.visitFootnotes(child, label);
      });
    }
  }

  private static safeUrl(this: void, value: string): string {
    return /^(?:https?:\/\/|#[A-Za-z0-9_-])/i.test(value) ? value : '';
  }
}
