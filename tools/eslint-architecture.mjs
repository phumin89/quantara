import path from 'node:path';

export default {
  rules: {
    'file-ownership': {
      meta: {
        type: 'problem',
        schema: [],
        messages: {
          ownership: 'Declare only one named type or React component per file.',
          matchingName:
            'Name the file {{name}} to match its owned type/component.',
        },
      },
      create(context) {
        const owned = [];
        const filename = context.filename;
        return {
          ClassDeclaration(node) {
            if (node.id) {
              owned.push(node.id);
            }
          },
          TSInterfaceDeclaration(node) {
            owned.push(node.id);
          },
          TSEnumDeclaration(node) {
            owned.push(node.id);
          },
          TSTypeAliasDeclaration(node) {
            owned.push(node.id);
          },
          FunctionDeclaration(node) {
            if (
              filename.endsWith('.tsx') &&
              node.id &&
              /^[A-Z]/.test(node.id.name)
            ) {
              owned.push(node.id);
            }
          },
          VariableDeclarator(node) {
            if (
              filename.endsWith('.tsx') &&
              node.id.type === 'Identifier' &&
              /^[A-Z]/.test(node.id.name) &&
              ['ArrowFunctionExpression', 'FunctionExpression'].includes(
                node.init?.type,
              )
            ) {
              owned.push(node.id);
            }
          },
          'Program:exit'() {
            if (owned.length > 1) {
              for (const node of owned) {
                context.report({ node, messageId: 'ownership' });
              }
            }
            const basename = path.basename(filename).replace(/\.tsx?$/, '');
            for (const node of owned) {
              if (
                !['page', 'layout'].includes(basename) &&
                node.name !== basename
              ) {
                context.report({
                  node,
                  messageId: 'matchingName',
                  data: { name: `${node.name}${path.extname(filename)}` },
                });
              }
            }
          },
        };
      },
    },
  },
};
