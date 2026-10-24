"""Synthetic Visual Studio channels, manifests, VSIX and NuGet packages shaped like Microsoft's."""
import hashlib
import io
import json
import zipfile

LICENCE = "https://example.test/licence"
SDK_FOLDER = "10.0.26100.0"
MAIN_ID = "Microsoft.Windows.SDK.CPP"
X64_ID = "Microsoft.Windows.SDK.CPP.x64"


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def archive(members):
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as zip_file:
        for name, data in members.items():
            zip_file.writestr(name, data)
    return buffer.getvalue()


def _vsix(members):
    return archive({"[Content_Types].xml": b"<Types/>", "manifest.json": b"{}", "_rels/.rels": b"",
                    **{"Contents/" + name: data for name, data in members.items()}})


def crt_feed(base, prefix="", line="14.44", toolset="14.44.35226", redist="14.44.35211",
             build="17.14.1.1", manifest_build=None, manifest_sha=None, extra_headers=None,
             lib_toolset=None):
    """Files of a channel at `<prefix>/channel` serving the line's three CRT packages and some decoys."""
    tools = f"VC/Tools/MSVC/{toolset}"
    libs = f"VC/Tools/MSVC/{lib_toolset or toolset}"
    crt = f"VC/Redist/MSVC/{redist}/x64/Microsoft.VC143.CRT"
    patch = f"{line}.17.14"
    packages = {
        f"Microsoft.VC.{patch}.CRT.Headers.base": _vsix({f"{tools}/include/vcruntime.h": b"#pragma once\n",
                                                         f"{tools}/include/vector": b"// vector\n",
                                                         **(extra_headers or {})}),
        f"Microsoft.VC.{patch}.CRT.x64.Desktop.base": _vsix({f"{libs}/lib/x64/libcpmt.lib": b"lib"}),
        f"Microsoft.VC.{patch}.CRT.x64.Store.base": _vsix({f"{libs}/lib/x64/msvcprt.lib": b"lib",
                                                           f"{libs}/lib/x64/vcruntime.lib": b"lib",
                                                           f"{libs}/lib/x64/oldnames.lib": b"lib"}),
        f"Microsoft.VC.{patch}.CRT.Redist.X64.base": _vsix({f"{crt}/vcruntime140.dll": b"dll",
                                                            f"{crt}/msvcp140.dll": b"dll"}),
        f"Microsoft.VC.{patch}.Tools.HostX64.TargetX64.base": _vsix({f"{tools}/bin/cl.exe": b"exe"}),
        "Microsoft.VC.14.43.17.13.CRT.Headers.base": _vsix({"VC/Tools/MSVC/14.43.1/include/old.h": b""}),
    }
    files = {}
    entries = []
    for package_id, data in packages.items():
        path = f"{prefix}/{package_id}.vsix"
        files[path] = data
        entries.append({"id": package_id, "version": toolset, "type": "Vsix",
                        "payloads": [{"fileName": package_id + ".vsix", "url": base + path,
                                      "sha256": sha(data).lower(), "size": len(data)}]})
    entries.append({"id": f"Microsoft.VC.{patch}.CRT.Headers.Resources.base", "version": toolset,
                    "type": "Vsix", "language": "en-US", "payloads": []})
    manifest = json.dumps({"manifestVersion": "1.1", "info": {"buildVersion": manifest_build or build},
                           "packages": entries}).encode()
    files[f"{prefix}/vsman"] = manifest
    files[f"{prefix}/channel"] = json.dumps({"channelItems": [
        {"id": "Microsoft.VisualStudio.Manifests.VisualStudio", "version": build, "type": "Manifest",
         "payloads": [{"fileName": "VisualStudio.vsman", "url": base + prefix + "/vsman",
                       "sha256": manifest_sha or sha(manifest).lower(), "size": len(manifest)}]},
        {"id": "Microsoft.VisualStudio.Product.BuildTools", "version": build, "type": "ChannelProduct",
         "localizedResources": [{"language": "en-us", "license": LICENCE}]},
    ]}).encode("utf-8-sig")
    return files


def sdk_packages(version="10.0.26100.9169"):
    """Files of a NuGet flat container holding the two Windows SDK packages, and their pins."""
    include = f"c/Include/{SDK_FOLDER}"
    bin_x64 = f"c/bin/{SDK_FOLDER}/x64"
    main = archive({**{f"{include}/{part}/{part}.h": b"" for part in ("ucrt", "um", "shared", "winrt", "cppwinrt")},
                    f"{bin_x64}/rc.exe": b"rc", f"{bin_x64}/rcdll.dll": b"rcdll", f"{bin_x64}/mt.exe": b"mt",
                    f"{bin_x64}/ServicingCommon.dll": b"dll", f"{bin_x64}/midlrtmd.dll": b"dll",
                    f"{bin_x64}/signtool.exe": b"no", f"c/bin/{SDK_FOLDER}/x86/rc.exe": b"no",
                    "c/Source/x.c": b"no", "Microsoft.Windows.SDK.CPP.nuspec": b"<package/>"})
    x64 = archive({"c/ucrt/x64/ucrt.lib": b"lib", "c/um/x64/kernel32.lib": b"lib",
                   "c/um/x64/user32.lib": b"lib", "Microsoft.Windows.SDK.CPP.x64.nuspec": b"<package/>"})
    files = {}
    pins = []
    for package_id, data in ((MAIN_ID, main), (X64_ID, x64)):
        lower = package_id.lower()
        files[f"/{lower}/{version}/{lower}.{version}.nupkg"] = data
        pins.append((package_id, sha(data)))
    return files, pins
