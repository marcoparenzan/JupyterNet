# pandas in JupyterNet notebooks (PySharp kernel)

The PySharp kernel runs `import pandas as pd` on **PySharp.Pandas**, a native .NET implementation of pandas 3.0 (no Python installation, no
CPython extension). Printing, results and error types are checked against real pandas 3.0.6 — see `PySharp/PANDAS_PLAN.md` and the
`samples/pandas_*.py` scripts, whose output is byte-identical to CPython + pandas.

## Setup

Publish the PySharp kernel as described in [cvintro-course.md](cvintro-course.md) (the same publish carries pandas, numpy, matplotlib, OpenCV and torch):

```powershell
dotnet publish D:\dev\2026\repos\PySharp\src\JupyterNet.Kernels.PySharp -c Release -r win-x64 --self-contained false `
    -o D:\dev\2026\repos\PySharp\src\JupyterNet.Kernels.PySharp\bin\publish
```

Stop any running `JupyterNet.Host` first (VS Code restarts it on the next cell). Then open a notebook with the **JupyterNet** notebook type and use the
`pysharp` language for the code cells.

## What a cell shows

- The value of a cell's **last expression** is echoed like in Jupyter: a **DataFrame as an HTML table**, any other object by its `repr`
  (`None` shows nothing). A cell that ends with `df` or `df.groupby('k').sum()` is therefore enough.
- `display(obj1, obj2, ...)` shows several values from one cell (`display(df.head(), df.describe())`).
- `print(df)` gives the plain-text table, exactly as pandas prints it.
- **Plots**: `df.plot()`, `df.plot.bar()`, `df.hist()` ... draw through PySharp.Matplotlib; any figure still open at the end of the cell is shown inline as a PNG
  (the same mechanism as `plt.show()`).
- Display options work as in pandas (`pd.set_option('display.max_rows', 100)`); the kernel starts with `display.max_columns = 20`, Jupyter's default.
  Options are per kernel session.

```python
import pandas as pd
sales = pd.read_csv("sales.csv")
sales["revenue"] = sales["units"] * sales["price"]
by_region = sales.groupby("region")["revenue"].agg(["count", "sum", "mean"]).round(2)
by_region            # <- shown as an HTML table
```

```python
by_region["sum"].plot.bar(title="Revenue by region")    # <- shown as an inline image
```

## What is supported

Series/DataFrame/Index/MultiIndex, selection (`loc`/`iloc`/masks), arithmetic with index alignment, reductions, missing data, sorting, `apply`/`map`/`agg`,
the `.str` accessor, `groupby`, `merge`/`join`/`concat`, `pivot_table`/`pivot`/`melt`/`crosstab`/`stack`/`unstack`/`get_dummies`, `rolling`/`expanding`/`ewm`/`rank`,
categoricals (`category` dtype, `pd.cut`, `pd.qcut`), dates, time zones and periods (`Timestamp`, `pd.to_datetime`, `pd.date_range`, `.dt`, `resample`, `rolling('7D')`, `parse_dates=`, `tz_convert`, `Period`), `query`/`eval`, `read_csv`/`to_csv`/`read_json`/`to_json`/`to_dict`, and the plot kinds `line`, `bar`, `barh`, `hist`, `scatter`, `area`, `pie`, `box`, `kde`.

## Limits worth knowing

- Not implemented: nullable dtypes, Excel/Parquet/SQL IO, MultiIndex columns in `stack`/`unstack`. Unsupported features raise `NotImplementedError` with the name of the missing piece.
- `.values` / `np.asarray` work for numeric and bool columns (NDSharp has no object arrays).
- Plot rendering is not pixel-identical to matplotlib's Agg backend (layout agrees to about a pixel); numbers and labels are the same.
- HTML output is produced for frames with a flat index and flat columns; a frame with a MultiIndex is shown as text.
