import { ToggleButton, ToggleButtonGroup } from "@mui/material";
import GridViewOutlinedIcon from "@mui/icons-material/GridViewOutlined";
import AccountBalanceWalletOutlinedIcon from "@mui/icons-material/AccountBalanceWalletOutlined";
import CheckCircleOutlinedIcon from "@mui/icons-material/CheckCircleOutlined";

export default function PortfolioFilters({
  filter,
  onChange,
}: {
  filter: "All" | "Holding" | "Closed";
  onChange: (filter: "All" | "Holding" | "Closed") => void;
}) {
  return (
    <ToggleButtonGroup
      exclusive
      value={filter}
      onChange={(_, value: "All" | "Holding" | "Closed" | null) => {
        if (value) {
          onChange(value);
        }
      }}
      aria-label="Portfolio filter"
      size="small"
      sx={{
        bgcolor: "#f3f3f3",
        borderRadius: "6px",
        p: 0.5,
        width: "fit-content",
        "& .MuiToggleButtonGroup-grouped": {
          border: 0,
          borderRadius: "6px",
          minHeight: 44,
          px: 1.25,
          gap: 0.75,
          textTransform: "none",
          fontSize: 14,
        },
      }}
    >
      {(["All", "Holding", "Closed"] as const).map((value) => (
        <ToggleButton
          key={value}
          value={value}
          sx={{
            border: 0,
            ...(value === "All"
              ? {
                  color: "#525252",
                  "&.Mui-selected": {
                    bgcolor: "#fff",
                    color: "#141414",
                    boxShadow: "inset 0 0 0 1px #bdbdbd",
                  },
                }
              : value === "Holding"
                ? {
                    color: "#166534",
                    "&.Mui-selected": {
                      bgcolor: "#dcfce7",
                      color: "#166534",
                      boxShadow: "inset 0 0 0 1px #166534",
                    },
                  }
                : {
                    color: "#6b21a8",
                    "&.Mui-selected": {
                      bgcolor: "#f3e8ff",
                      color: "#6b21a8",
                      boxShadow: "inset 0 0 0 1px #6b21a8",
                    },
                  }),
          }}
        >
          {value === "All" ? (
            <GridViewOutlinedIcon aria-hidden="true" sx={{ fontSize: 18 }} />
          ) : value === "Holding" ? (
            <AccountBalanceWalletOutlinedIcon
              aria-hidden="true"
              sx={{ fontSize: 18 }}
            />
          ) : (
            <CheckCircleOutlinedIcon aria-hidden="true" sx={{ fontSize: 18 }} />
          )}
          {value}
        </ToggleButton>
      ))}
    </ToggleButtonGroup>
  );
}
