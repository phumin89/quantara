import { Box, Typography } from "@mui/material";

export default function SummaryMetric({
  testId,
  label,
  value,
  caption,
}: {
  testId: string;
  label: string;
  value: string;
  caption: string;
}) {
  return (
    <Box
      sx={{ flex: 1, minWidth: 0, px: { xs: 0, sm: 2 }, py: { xs: 1, sm: 0 } }}
    >
      <Typography sx={{ fontSize: 12, color: "#707070", fontWeight: 600 }}>
        {label}
      </Typography>
      <Typography
        data-testid={testId}
        sx={{
          mt: 0.5,
          fontSize: 24,
          lineHeight: 1.2,
          fontWeight: 650,
          fontVariantNumeric: "tabular-nums",
        }}
      >
        {value}
      </Typography>
      <Typography sx={{ mt: 0.5, fontSize: 12, color: "#707070" }}>
        {caption}
      </Typography>
    </Box>
  );
}
