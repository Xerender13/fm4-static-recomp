#pragma once

#include <rex/runtime.h>

namespace fm4_static_recomp {

// Installs Disc 2's content packages from the directory named by the
// fm4_disc2_packages cvar into the user data, as the game's "Install Disc 2"
// would have (that path asks for a disc swap, which never completes here), and
// every marketplace package under the fm4_dlc_packages directory.
// Packages already installed are skipped.
void InstallContentPackages(rex::Runtime& runtime);

}  // namespace fm4_static_recomp
