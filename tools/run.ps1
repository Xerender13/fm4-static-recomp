[CmdletBinding()]
param(
    [ValidateSet('Release', 'RelWithDebInfo')]
    [string]$Configuration = 'Release',
    [string]$StateRoot,
    # Stop the game after this many seconds; 0 leaves it running.
    [int]$TimeoutSeconds = 0,
    # Renderer profile plus resolve dumps on request into .local\dumps (see dump-watch.ps1).
    [switch]$Dumps,
    # Every rendering shortcut that has a switch off, every resolve read back, console
    # graphics settings: tells a rendering bug of ours from the game's own look.
    [switch]$Compat,
    # Records the gamepad into this file as an input script (src\scripted_input.h) for replays;
    # a relative path is relative to the project folder.
    [string]$RecordInput,
    [Parameter(ValueFromRemainingArguments)] [string[]]$GameArguments = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$game = [IO.Path]::GetFullPath((Join-Path $root '../extracted/fm4'))
if (-not $StateRoot) { $StateRoot = Join-Path $root '.local/state' }
$StateRoot = [IO.Path]::GetFullPath($StateRoot)
$executable = Join-Path $root "out/build/win-amd64-$($Configuration.ToLowerInvariant())/fm4_native.exe"
foreach ($directory in 'user', 'update', 'cache', 'logs') {
    [void](New-Item -ItemType Directory -Force -Path (Join-Path $StateRoot $directory))
}
$log = Join-Path $StateRoot 'logs/runtime.log'
if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force }

$arguments = @(
    "--game_data_root=$game",
    "--user_data_root=$(Join-Path $StateRoot 'user')",
    "--update_data_root=$(Join-Path $StateRoot 'update')",
    "--cache_root=$(Join-Path $StateRoot 'cache')",
    "--log_file=$log",
    '--gpu_backend=vulkan'
)
if ($Compat) {
    foreach ($option in '--fh1_async_readbacks=false', '--fh1_readback_all_resolves=true',
        '--fh1_surfaceless_clears=false', '--fh1_color_overwrite_claims=false',
        '--fh1_scrambled_stencil_claims=false', '--fh1_scaled_msaa_single_sample=false',
        '--gpu_record_thread=false', '--clear_memory_page_state=true', '--texture_cache_memory_limit_soft=1024',
        '--anisotropic_override=-1', '--fm4_level_of_detail=console', '--fm4_shadow_map_size=512',
        '--fm4_reflection_cube_size=256', '--fm4_full_rate_scenes=false') {
        $name = $option.Split('=')[0]
        if (-not ($GameArguments | Where-Object { $_.Split('=')[0] -eq $name })) { $GameArguments += $option }
    }
}
# The SDK defaults re-upload every CPU-written page each frame (~500 render pass
# breaks per race frame) and present at most 60 fps; Pinyon's config turns both off.
# A race renders --fm4_frame_rate frames a second (120 unless the GRAPHICS page or the
# option says otherwise), the menus about 120 (fm4_menu_vblank_hz). A repeated option stops
# the launch, so a default gives way to the same option among the game arguments.
# --gpu_record_thread records the Vulkan draws on a thread of their own (a 2x race +12 %).
# The texture cache evicts only above its soft limit (SDK default 1 GB), so menu and showroom
# textures stay in VRAM; a race needs about 300 MB at 2x, and 256 saved 300 MB with no reloads.
# --fh1_async_readbacks keeps resolve readbacks from draining the GPU in races (smooth at 3x),
# at a small risk of a texture read before its readback lands; the menus wait regardless.
foreach ($default in '--clear_memory_page_state=false', '--host_present_fps_limit=0', '--gpu_record_thread=true',
    '--texture_cache_memory_limit_soft=256', '--fh1_async_readbacks=true') {
    $name = $default.Split('=')[0]
    if (-not ($GameArguments | Where-Object { $_.Split('=')[0] -eq $name })) { $arguments += $default }
}
$arguments += $GameArguments
if ($Dumps) {
    $dumpRoot = Join-Path $root '.local\dumps'
    [void](New-Item -ItemType Directory -Force -Path $dumpRoot)
    [void](New-Item -ItemType Directory -Force -Path (Join-Path $dumpRoot 'shaders'))
    $arguments += '--fh1_native_gpu_profile=true', "--fh1_resolve_dump_dir=$dumpRoot", '--fh1_resolve_dump_frame=-1',
        "--dump_shaders=$(Join-Path $dumpRoot 'shaders')"
}
$env:FM4_NATIVE_INPUT_RECORD = if ($RecordInput) { [IO.Path]::GetFullPath($RecordInput, $root) } else { '' }
$process = Start-Process -FilePath $executable -ArgumentList $arguments `
    -WorkingDirectory (Split-Path $executable -Parent) -PassThru
$env:FM4_NATIVE_INPUT_RECORD = ''
if ($TimeoutSeconds -gt 0) {
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill()
        $process.WaitForExit()
        "stopped after $TimeoutSeconds s"
    }
    else { "exited with code $($process.ExitCode)" }
}
else { "started fm4_native (PID $($process.Id)); log: $log" }
