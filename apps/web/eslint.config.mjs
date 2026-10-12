import { defineConfig, globalIgnores } from "eslint/config";
import react from "eslint-plugin-react";
import reactHooks from "eslint-plugin-react-hooks";
import jsxA11y from "eslint-plugin-jsx-a11y";
import tseslint from "typescript-eslint";
import architecture from "../../tools/eslint-architecture.mjs";
import stylistic from "@stylistic/eslint-plugin";

const files = [
  "app/**/*.{ts,tsx}",
  "features/**/*.{ts,tsx}",
  "shared/**/*.{ts,tsx}",
];
export default defineConfig([
  { ...react.configs.flat.recommended, files },
  { ...react.configs.flat["jsx-runtime"], files },
  { ...reactHooks.configs.flat.recommended, files },
  { ...jsxA11y.flatConfigs.recommended, files },
  ...tseslint.configs.strictTypeChecked.map((config) => ({ ...config, files })),
  {
    files,
    plugins: { quantara: architecture, "@stylistic": stylistic },
    languageOptions: {
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
    settings: { react: { version: "detect" } },
    rules: {
      "quantara/file-ownership": "error",
      "@stylistic/no-multiple-empty-lines": [
        "error",
        { max: 1, maxBOF: 0, maxEOF: 0 },
      ],
      "@stylistic/padding-line-between-statements": [
        "error",
        { blankLine: "always", prev: "directive", next: "*" },
        { blankLine: "always", prev: "import", next: "*" },
        { blankLine: "any", prev: "import", next: "import" },
        { blankLine: "always", prev: "block-like", next: "*" },
        { blankLine: "always", prev: "*", next: "return" },
      ],
      "@typescript-eslint/no-explicit-any": "error",
      "@typescript-eslint/no-non-null-assertion": "error",
      "react/prop-types": "off",
      "@typescript-eslint/ban-ts-comment": [
        "error",
        {
          "ts-ignore": true,
          "ts-nocheck": true,
          "ts-expect-error": "allow-with-description",
          minimumDescriptionLength: 10,
        },
      ],
      curly: ["error", "all"],
      eqeqeq: ["error", "always", { null: "ignore" }],
    },
  },
  globalIgnores([".next/**", "next-env.d.ts"]),
]);
