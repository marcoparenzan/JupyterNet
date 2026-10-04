# Running the cvintro course (61 notebooks) with the PySharp kernel

The notebooks of `sbirchfield.github.io/cvintro` (numpy → OpenCV → PyTorch → pretrained vision models) run on
the PySharp kernel: all 61 notebooks, 424 code cells (report: `PySharp/NOTEBOOKS_RUN.md`, plan and known
divergences: `PySharp/NOTEBOOKS_PLAN.md`). No Python installation is needed; the libraries behind
`numpy`, `cv2`, `pywt`, `matplotlib`, `torch` and `torchvision` are native .NET libraries.

## One-time setup

1. Publish the PySharp kernel for your platform (it carries libtorch, OpenCV and Skia natives; without `-r` the
   publish contains every platform's natives, about 1.3 GB instead of about 360 MB):

   ```powershell
   dotnet publish D:\dev\2026\repos\PySharp\src\JupyterNet.Kernels.PySharp -c Release -r win-x64 --self-contained false `
       -o D:\dev\2026\repos\PySharp\src\JupyterNet.Kernels.PySharp\bin\publish
   ```

   Stop any running `JupyterNet.Host` first (VS Code restarts it on the next cell), otherwise the DLLs are locked.
   This is the extension's default `jupyternet.kernelPaths` entry, so there is nothing else to configure. The host's
   plugin loader probes the plugin folder for native libraries.
2. In VS Code, open a notebook and pick the **JupyterNet** notebook type; cells of the course use the `pysharp`
   language. (For files written for Jupyter, set each code cell's `languageId` to `pysharp`.)

## Running

- **In VS Code:** run the cells. `plt.show()` and any figure still open at the end of a cell appear as inline PNG
  output (the kernel calls `display_image` through the output sink).
- **From the command line**, with the engine's CLI:

  ```powershell
  dotnet run -c Release --project src\JupyterNet.Cli -- run <notebook.ipynb> `
      --kernel-paths D:\dev\2026\repos\PySharp\src\JupyterNet.Kernels.PySharp\bin\publish --fail-fast
  ```

  Run it from the folder that contains the notebooks: several cells open images with relative paths (`../img/...`).
- **Whole course, with a report:** `PySharp/tools/NotebookRunner` runs every notebook in one session each and writes
  `NOTEBOOKS_RUN.md`; build and run it in **Release** (Debug is about 10x slower and times out on the training
  lessons). A complete run takes about 20 minutes.

## What gets downloaded (first use only, cached)

| What | Used by | Size | Cache |
|---|---|---|---|
| CIFAR-10 | lessons 35, 36, 38, 39 | 163 MB | `~/.cache/cvintro` |
| Pretrained weights (resnet18, FCN-ResNet50, Faster R-CNN, Mask R-CNN) | 38, 41, 42, 43 | 46 – 170 MB each | `~/.cache/torch/hub/checkpoints` (shared with real PyTorch) |
| YuNet face detector, a COCO sample image | 41 | 230 KB, 160 KB | `~/.cache/cvintro` |

## Limits worth knowing

- CPU only: `torch.cuda` reports unavailable, as do MPS and `device='cuda'`.
- Rendering is not pixel-identical to matplotlib's Agg (layout agrees to about one pixel); numeric output of numpy, OpenCV,
  PyWavelets, torch and torchvision is verified against the real libraries (`PySharp/src/PySharp.Tests/Oracle`).
- Not implemented: matplotlib `twinx`, `pcolormesh`, `plot_surface`, pdf/svg `savefig`; torchvision model families the course does not use;
  PIL inputs to transforms.
