<img src="Documentation~/images/image.png" align="middle" width="2200"/>

# LEGO Model Importer

The purpose of LEGO Model Importer is to help you import LEGO models into Unity or build new models using our Connectivity System.

## Features

- **Import models from Bricklink Studio**
- **Import LDR files**
- **Various optimization options for the imported model**
- **In Unity Editor Connectivity system to alter or create a new model**

## Install with Unity Package Manager

Requires Unity 2021.2.7f1 or later and Git installed on your computer.

1. Open **Window > Package Manager**.
2. Click **+ > Add package from git URL**.
3. Enter `https://github.com/ESCG-Lewes/com.unity.lego.modelimporter-8.0.2.git`.
4. Click **Add**.

The package declares dependencies on LEGO Materials 3.0.0, Universal RP 12.1.2,
and Unity UI 1.0.0. Unity must be able to resolve these from its configured
registries. If LEGO Materials cannot be resolved, install that package separately
from its available source before adding this importer. Git dependencies belong
in the consuming project's `Packages/manifest.json`, not this package's dependencies.

This replaces the existing `com.unity.lego.modelimporter` package; do not keep a
second copy under Assets. Git LFS is not needed: the larger part archives are
stored as split files and the importer reconstructs them automatically.

To test a local checkout before publishing, choose **Add package from disk** and
select this repository's `package.json`. Repository changes must be committed and
pushed before a Git URL installation can fetch them.

## Instructions

For documentation see [Documentation](Documentation~/LEGO%20Model%20Importer.md)

The current version requires **Unity 2021.2+**.

## Community and Feedback

For any feedback or bug reports please reach out to LEGO Games.

Email: [LEGOUnityPackages@lego.com](mailto:LEGOUnityPackages@lego.com)

## License & Terms of Agreement

LEGO Companion License (“License”) Software Copyright © 2020 LEGO System A/S

For licensing details see [LICENSE.md](LICENSE.md)


