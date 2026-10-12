const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');
const root = path.resolve('apps/web');
function scan(dir) {
  return fs
    .readdirSync(dir, { withFileTypes: true })
    .flatMap((item) =>
      item.isDirectory() && !['node_modules', '.next'].includes(item.name)
        ? scan(path.join(dir, item.name))
        : item.isFile() && /\.tsx?$/.test(item.name)
          ? [path.join(dir, item.name)]
          : [],
    );
}
let count = 0;
for (const file of scan(root).filter(
  (file) => !file.endsWith('next-env.d.ts'),
)) {
  const text = fs.readFileSync(file, 'utf8');
  const source = ts.createSourceFile(
    file,
    text,
    ts.ScriptTarget.Latest,
    true,
    file.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS,
  );
  const components = [],
    types = [],
    imports = [];
  function visit(node) {
    if (
      ts.isFunctionDeclaration(node) ||
      (ts.isVariableDeclaration(node) &&
        node.initializer &&
        (ts.isArrowFunction(node.initializer) ||
          ts.isFunctionExpression(node.initializer)))
    )
      throw Error('Application behavior must be class-based: ' + file);
    if (
      file.endsWith('.tsx') &&
      ts.isFunctionDeclaration(node) &&
      node.name &&
      /^[A-Z]/.test(node.name.text)
    )
      components.push(node.name.text);
    if (
      file.endsWith('.tsx') &&
      ts.isVariableDeclaration(node) &&
      ts.isIdentifier(node.name) &&
      /^[A-Z]/.test(node.name.text) &&
      node.initializer &&
      (ts.isArrowFunction(node.initializer) ||
        ts.isFunctionExpression(node.initializer))
    )
      components.push(node.name.text);
    if (
      ts.isClassDeclaration(node) ||
      ts.isInterfaceDeclaration(node) ||
      ts.isEnumDeclaration(node) ||
      ts.isTypeAliasDeclaration(node)
    )
      types.push(node.name?.text);
    if (
      ts.isImportDeclaration(node) &&
      ts.isStringLiteral(node.moduleSpecifier)
    )
      imports.push(node.moduleSpecifier.text);
    ts.forEachChild(node, visit);
  }
  visit(source);
  if (components.length > 1) throw Error('One component per file: ' + file);
  if (types.length > 1) throw Error('One type per file: ' + file);
  const name = path.basename(file).replace(/\.tsx?$/, '');
  if (
    components.length &&
    !['page', 'layout'].includes(name) &&
    components[0] !== name
  )
    throw Error('Component name must match file: ' + file);
  if (types.length && types[0] !== name)
    throw Error('Type name must match file: ' + file);
  for (const target of imports) {
    if (target.startsWith('.')) {
      const resolved = path.resolve(path.dirname(file), target);
      if (!resolved.startsWith(root + path.sep))
        throw Error('Web cannot import another service: ' + file);
      if (
        file.includes(path.sep + 'shared' + path.sep) &&
        resolved.includes(path.sep + 'features' + path.sep)
      )
        throw Error('Shared code must not depend on feature: ' + file);
      if (
        file.includes(path.sep + 'components' + path.sep) &&
        (resolved.includes(path.sep + 'api' + path.sep) ||
          resolved.includes(path.sep + 'services' + path.sep)) &&
        path.basename(file) !== 'CryptoPage.tsx'
      )
        throw Error(
          'Only the screen container can call feature API clients: ' + file,
        );
    }
  }
  count++;
}
console.log(
  `Architecture: ${count} frontend files satisfy type/component ownership and dependency boundaries.`,
);
