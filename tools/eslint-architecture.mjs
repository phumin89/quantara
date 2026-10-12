import path from 'node:path';

export default {
  rules: {
    'injected-services': {
      meta: {
        type: 'problem',
        schema: [],
        messages: {
          injected:
            'Components must receive services through dependency injection. Create service instances in ServiceProvider.',
        },
      },
      create(context) {
        const filename = context.filename.replaceAll('\\', '/');
        if (
          !filename.endsWith('.tsx') ||
          filename.includes('/app/providers/')
        ) {
          return {};
        }
        return {
          NewExpression(node) {
            if (
              node.callee.type === 'Identifier' &&
              /(Service|ServiceContainer)$/.test(node.callee.name)
            ) {
              context.report({ node, messageId: 'injected' });
            }
          },
        };
      },
    },
    'class-based': {
      meta: {
        type: 'problem',
        schema: [],
        messages: {
          classBased:
            'Put application behavior in a class method. Standalone functions and hooks are not allowed.',
        },
      },
      create(context) {
        return {
          ExportDefaultDeclaration(node) {
            if (
              ['ArrowFunctionExpression', 'FunctionExpression'].includes(
                node.declaration.type,
              )
            ) {
              context.report({ node, messageId: 'classBased' });
            }
          },
          FunctionDeclaration(node) {
            context.report({ node, messageId: 'classBased' });
          },
          VariableDeclarator(node) {
            if (
              ['ArrowFunctionExpression', 'FunctionExpression'].includes(
                node.init?.type,
              )
            ) {
              context.report({ node, messageId: 'classBased' });
            }
          },
          ImportDeclaration(node) {
            for (const specifier of node.specifiers) {
              if (
                specifier.type === 'ImportSpecifier' &&
                specifier.imported.type === 'Identifier' &&
                /^use[A-Z]/.test(specifier.imported.name)
              ) {
                context.report({ node: specifier, messageId: 'classBased' });
              }
            }
          },
        };
      },
    },
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
