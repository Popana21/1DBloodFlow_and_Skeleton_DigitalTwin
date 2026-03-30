param(
  [string]$InputJson = "Assets/StreamingAssets/vessels.json",
  [string]$OutDot = "Docs/vessel_tree_from_json.dot",
  [string]$OutSvg = "Docs/vessel_tree_from_json.svg",
  [bool]$MirrorFrontView = $true
)

$ErrorActionPreference = "Stop"

if (!(Test-Path $InputJson)) {
  throw "Input JSON not found: $InputJson"
}

$data = Get-Content -Raw -Path $InputJson | ConvertFrom-Json
$vessels = @($data.vessels)
if ($vessels.Count -eq 0) {
  throw "No vessels found in JSON."
}

function Get-SideBucket([string]$label) {
  if ([string]::IsNullOrEmpty($label)) { return "center" }
  $l = $label.ToLowerInvariant()

  if ($l -match "(_r$|_r_|_right|\.r$|-r$)") { return "right_anat" }
  if ($l -match "(_l$|_l_|_left|\.l$|-l$)") { return "left_anat" }
  if ($l -match "^right_") { return "right_anat" }
  if ($l -match "^left_") { return "left_anat" }
  return "unknown"
}

function Is-TrunkLabel([string]$label) {
  if ([string]::IsNullOrEmpty($label)) { return $false }
  $l = $label.ToLowerInvariant()
  return $l -match "^(aortic_arch_|thoracic_aorta_|abdominal_aorta_)"
}

function Get-BodyPart([string]$label) {
  if ([string]::IsNullOrEmpty($label)) { return "other" }
  $l = $label.ToLowerInvariant()

  if ($l -match "(external_carotid|internal_carotid)") { return "head" }
  if ($l -match "(common_carotid|vertebral)") { return "neck" }
  if ($l -match "(aortic_arch|brachiocephalic|subclavian_.*_i$)") { return "upper_chest" }
  if ($l -match "(thoracic_aorta|posterior_intercostal)") { return "chest" }
  if ($l -match "(abdominal_aorta|celiac|hepatic|splenic|gastric|mesenteric|renal)") { return "spine" }
  if ($l -match "(common_iliac|external_iliac|internal_iliac)") { return "hips" }
  if ($l -match "(axillary|brachial)") { return "upper_arm" }
  if ($l -match "(radial|ulnar|interosseous|subclavian_.*_ii$)") { return "lower_arm" }
  if ($l -match "(femoral|profunda_femoris|popliteal)") { return "upper_leg" }
  if ($l -match "(tibial|tibiofibular)") { return "lower_leg" }
  return "other"
}

# Body-part colors (ignoring side).
$bodyPartColor = @{
  "head"        = "#EF4444"
  "neck"        = "#F59E0B"
  "upper_chest" = "#0F766E"
  "chest"       = "#0E7490"
  "spine"       = "#7C3AED"
  "hips"        = "#D97706"
  "upper_arm"   = "#2563EB"
  "lower_arm"   = "#1D4ED8"
  "upper_leg"   = "#059669"
  "lower_leg"   = "#0F766E"
  "other"       = "#6B7280"
}

# Build adjacency map
$bySn = @{}
$allNodes = New-Object "System.Collections.Generic.HashSet[int]"
foreach ($e in $vessels) {
  [void]$allNodes.Add([int]$e.sn)
  [void]$allNodes.Add([int]$e.tn)

  if (-not $bySn.ContainsKey([int]$e.sn)) {
    $bySn[[int]$e.sn] = New-Object System.Collections.ArrayList
  }
  [void]$bySn[[int]$e.sn].Add($e)
}

# Discover trunk path (follow trunk-labeled edges from root node 1)
$trunkEdges = New-Object "System.Collections.Generic.HashSet[string]"
$trunkNodes = New-Object "System.Collections.Generic.HashSet[int]"
$cur = 1
[void]$trunkNodes.Add($cur)
for ($i = 0; $i -lt 128; $i++) {
  if (-not $bySn.ContainsKey($cur)) { break }

  $next = $null
  foreach ($cand in $bySn[$cur]) {
    if (Is-TrunkLabel $cand.label) { $next = $cand; break }
  }

  if ($null -eq $next) { break }

  $k = "{0}->{1}" -f [int]$next.sn, [int]$next.tn
  [void]$trunkEdges.Add($k)
  $cur = [int]$next.tn
  [void]$trunkNodes.Add($cur)
}

# Infer node side groups by propagating from root
$nodeGroup = @{}
$nodeGroup[1] = "center"

$updated = $true
$pass = 0
while ($updated -and $pass -lt 10) {
  $updated = $false
  $pass++

  foreach ($e in $vessels) {
    $sn = [int]$e.sn
    $tn = [int]$e.tn

    $parentGroup = if ($nodeGroup.ContainsKey($sn)) { $nodeGroup[$sn] } else { "center" }
    $bucket = Get-SideBucket $e.label

    $g = "center"
    if (Is-TrunkLabel $e.label) {
      $g = "center"
    }
    elseif ($bucket -eq "right_anat") {
      $g = if ($MirrorFrontView) { "left" } else { "right" }
    }
    elseif ($bucket -eq "left_anat") {
      $g = if ($MirrorFrontView) { "right" } else { "left" }
    }
    else {
      $g = $parentGroup
    }

    if (-not $nodeGroup.ContainsKey($tn) -or $nodeGroup[$tn] -ne $g) {
      $nodeGroup[$tn] = $g
      $updated = $true
    }
  }
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("digraph VesselTree {")
$lines.Add("  graph [rankdir=TB, splines=spline, newrank=true, overlap=false, nodesep=0.35, ranksep=0.75, pad=0.15];")
$lines.Add("  node  [shape=circle, width=0.24, fixedsize=true, fontsize=10, fontname=""Helvetica"", color=""#6B7280"", penwidth=1.0];")
$lines.Add("  edge  [fontsize=8, fontname=""Helvetica"", color=""#4B5563"", arrowsize=0.6, penwidth=1.0];")
$lines.Add("  labelloc=""t"";")
$lines.Add("  label=""OpenBF Vessel Tree (Graphviz DOT from vessels.json)"";")
$lines.Add("  ordering=out;")
$lines.Add("  // Spline routing helps show the aortic arch and branch curvature.")
$lines.Add("  splines=spline;")
$lines.Add("")
$lines.Add("  // Layout guide nodes to keep arm subtrees on opposite sides of the trunk.")
$lines.Add("  TrunkGuideLeft  [shape=point, width=0.01, label="""", style=invis, group=""left_guide""];")
$lines.Add("  TrunkGuideRight [shape=point, width=0.01, label="""", style=invis, group=""right_guide""];")
$lines.Add("  { rank=same; TrunkGuideLeft; N2; TrunkGuideRight; }")
$lines.Add("  TrunkGuideLeft -> N2 [style=invis, weight=200, minlen=1, constraint=true];")
$lines.Add("  N2 -> TrunkGuideRight [style=invis, weight=200, minlen=1, constraint=true];")
$lines.Add("  ArmGuideLeft  [shape=point, width=0.01, label="""", style=invis, group=""left_guide""];")
$lines.Add("  ArmGuideRight [shape=point, width=0.01, label="""", style=invis, group=""right_guide""];")
$lines.Add("  { rank=same; ArmGuideLeft; N3; N4; ArmGuideRight; }")
$lines.Add("  TrunkGuideLeft -> ArmGuideLeft [style=invis, weight=80, minlen=1, constraint=true];")
$lines.Add("  ArmGuideRight -> TrunkGuideRight [style=invis, weight=80, minlen=1, constraint=true];")
$lines.Add("  ArmGuideLeft -> N3 [style=invis, weight=120, minlen=1, constraint=true];")
$lines.Add("  N4 -> ArmGuideRight [style=invis, weight=120, minlen=1, constraint=true];")
$lines.Add("  // Force arm roots to opposite sides.")
if ($MirrorFrontView) {
  # Front-view mirror: body-right appears on diagram-left.
  $lines.Add("  ArmGuideLeft -> N5 [style=invis, weight=140, minlen=1, constraint=true];")
  $lines.Add("  ArmGuideRight -> N22 [style=invis, weight=140, minlen=1, constraint=true];")
}
else {
  $lines.Add("  ArmGuideRight -> N5 [style=invis, weight=140, minlen=1, constraint=true];")
  $lines.Add("  ArmGuideLeft -> N22 [style=invis, weight=140, minlen=1, constraint=true];")
}
$lines.Add("  // Hard ordering pins arm roots around trunk: left-pad -> N5 -> N2 -> N22 -> right-pad.")
$lines.Add("  SidePadLeft [shape=point, width=0.01, label="""", style=invis];")
$lines.Add("  SidePadRight [shape=point, width=0.01, label="""", style=invis];")
$lines.Add("  { rank=same; SidePadLeft; N5; N2; N22; SidePadRight; }")
$lines.Add("  SidePadLeft -> N5 [style=invis, weight=260, minlen=1, constraint=true];")
$lines.Add("  N5 -> N2 [style=invis, weight=220, minlen=1, constraint=true];")
$lines.Add("  N2 -> N22 [style=invis, weight=220, minlen=1, constraint=true];")
$lines.Add("  N22 -> SidePadRight [style=invis, weight=260, minlen=1, constraint=true];")
$lines.Add("")
$lines.Add("  // Keep posterior intercostals on their corresponding mirrored sides.")
if ($MirrorFrontView) {
  $lines.Add("  ArmGuideLeft -> N34 [style=invis, weight=80, minlen=1, constraint=true];")
  $lines.Add("  ArmGuideLeft -> N38 [style=invis, weight=80, minlen=1, constraint=true];")
  $lines.Add("  ArmGuideRight -> N36 [style=invis, weight=80, minlen=1, constraint=true];")
  $lines.Add("  ArmGuideRight -> N40 [style=invis, weight=80, minlen=1, constraint=true];")
}
else {
  $lines.Add("  ArmGuideRight -> N34 [style=invis, weight=80, minlen=1, constraint=true];")
  $lines.Add("  ArmGuideRight -> N38 [style=invis, weight=80, minlen=1, constraint=true];")
  $lines.Add("  ArmGuideLeft -> N36 [style=invis, weight=80, minlen=1, constraint=true];")
  $lines.Add("  ArmGuideLeft -> N40 [style=invis, weight=80, minlen=1, constraint=true];")
}
$lines.Add("")
$lines.Add("  // Node declarations (group helps keep left/center/right columns stable).")

foreach ($id in ($allNodes | Sort-Object)) {
  $g = if ($nodeGroup.ContainsKey($id)) { $nodeGroup[$id] } else { "center" }
  $fill = switch ($g) {
    "left"   { "#EEF2FF" }
    "right"  { "#ECFDF5" }
    default   { "#F9FAFB" }
  }
  $lines.Add(("  N{0} [label=""{0}"", group=""{1}"", style=filled, fillcolor=""{2}""];" -f $id, $g, $fill))
}

$lines.Add("")
$lines.Add("  // Strong trunk constraints keep the aorta vertical.")
foreach ($e in $vessels) {
  $sn = [int]$e.sn
  $tn = [int]$e.tn
  $key = "{0}->{1}" -f $sn, $tn
  if ($trunkEdges.Contains($key)) {
    $lbl = [string]$e.label
    $bp = Get-BodyPart $lbl
    $edgeColor = $bodyPartColor[$bp]
    $lines.Add(("  N{0} -> N{1} [label=""{2}"", color=""{3}"", penwidth=2.4, weight=40, minlen=1, constraint=true];" -f $sn, $tn, $lbl, $edgeColor))
  }
}

$lines.Add("")
$lines.Add("  // Keep neck/head vessels above parent nodes (anatomical upward direction).")
$lines.Add("  N6 -> N3 [style=invis, weight=120, minlen=1, constraint=true];")
$lines.Add("  N18 -> N4 [style=invis, weight=120, minlen=1, constraint=true];")
$lines.Add("  N7 -> N5 [style=invis, weight=120, minlen=1, constraint=true];")
$lines.Add("  N24 -> N22 [style=invis, weight=120, minlen=1, constraint=true];")
$lines.Add("  N16 -> N6 [style=invis, weight=140, minlen=1, constraint=true];")
$lines.Add("  N17 -> N6 [style=invis, weight=140, minlen=1, constraint=true];")
$lines.Add("  N20 -> N18 [style=invis, weight=140, minlen=1, constraint=true];")
$lines.Add("  N21 -> N18 [style=invis, weight=140, minlen=1, constraint=true];")
$lines.Add("  { rank=same; N16; N17; N20; N21; }")

$lines.Add("")
$lines.Add("  // Side branches: lower weight so they do not bend the trunk.")
foreach ($e in $vessels) {
  $sn = [int]$e.sn
  $tn = [int]$e.tn
  $key = "{0}->{1}" -f $sn, $tn
  if ($trunkEdges.Contains($key)) { continue }

  $lbl = [string]$e.label
  $bp = Get-BodyPart $lbl
  $edgeColor = $bodyPartColor[$bp]
  $isNeckOrHead = ($bp -eq "neck" -or $bp -eq "head")
  $isCarotidHead = ($lbl -match "external_carotid|internal_carotid")

  if ($isCarotidHead) {
    # Keep carotid head branches clean and side-consistent.
    $lines.Add(("  N{0} -> N{1} [label=""{2}"", color=""{3}"", weight=4, minlen=1, constraint=false, tailport=n, headport=s];" -f $sn, $tn, $lbl, $edgeColor))
  }
  elseif ($isNeckOrHead) {
    # Draw edges with relaxed rank constraints; reverse invisible constraints above place them upward.
    $lines.Add(("  N{0} -> N{1} [label=""{2}"", color=""{3}"", weight=3, minlen=1, constraint=false, tailport=n, headport=s];" -f $sn, $tn, $lbl, $edgeColor))
  }
  elseif ($lbl -eq "subclavian_R_I" -or $lbl -eq "subclavian_L_I") {
    # Encourage direct outward split into arm roots.
    $lines.Add(("  N{0} -> N{1} [label=""{2}"", color=""{3}"", weight=10, minlen=1, constraint=true];" -f $sn, $tn, $lbl, $edgeColor))
  }
  else {
    $lines.Add(("  N{0} -> N{1} [label=""{2}"", color=""{3}"", weight=2, minlen=1, constraint=true];" -f $sn, $tn, $lbl, $edgeColor))
  }
}

$lines.Add("")
$lines.Add("  // Legend")
$lines.Add("  Legend [shape=plain, fixedsize=false, width=0, height=0, margin=0, label=<")
$lines.Add("    <TABLE BORDER=""1"" CELLBORDER=""1"" CELLSPACING=""0"" CELLPADDING=""4"">")
$lines.Add("      <TR><TD COLSPAN=""2""><B>Body-Part Colors</B></TD></TR>")
$lines.Add(("      <TR><TD><FONT COLOR=""{0}"">&#9608;&#9608;&#9608;</FONT></TD><TD>Head</TD></TR>" -f $bodyPartColor["head"]))
$lines.Add(("      <TR><TD><FONT COLOR=""{0}"">&#9608;&#9608;&#9608;</FONT></TD><TD>Neck</TD></TR>" -f $bodyPartColor["neck"]))
$lines.Add(("      <TR><TD><FONT COLOR=""{0}"">&#9608;&#9608;&#9608;</FONT></TD><TD>Upper Chest</TD></TR>" -f $bodyPartColor["upper_chest"]))
$lines.Add(("      <TR><TD><FONT COLOR=""{0}"">&#9608;&#9608;&#9608;</FONT></TD><TD>Chest</TD></TR>" -f $bodyPartColor["chest"]))
$lines.Add(("      <TR><TD><FONT COLOR=""{0}"">&#9608;&#9608;&#9608;</FONT></TD><TD>Spine</TD></TR>" -f $bodyPartColor["spine"]))
$lines.Add(("      <TR><TD><FONT COLOR=""{0}"">&#9608;&#9608;&#9608;</FONT></TD><TD>Hips</TD></TR>" -f $bodyPartColor["hips"]))
$lines.Add(("      <TR><TD><FONT COLOR=""{0}"">&#9608;&#9608;&#9608;</FONT></TD><TD>Upper Arm</TD></TR>" -f $bodyPartColor["upper_arm"]))
$lines.Add(("      <TR><TD><FONT COLOR=""{0}"">&#9608;&#9608;&#9608;</FONT></TD><TD>Lower Arm</TD></TR>" -f $bodyPartColor["lower_arm"]))
$lines.Add(("      <TR><TD><FONT COLOR=""{0}"">&#9608;&#9608;&#9608;</FONT></TD><TD>Upper Leg</TD></TR>" -f $bodyPartColor["upper_leg"]))
$lines.Add(("      <TR><TD><FONT COLOR=""{0}"">&#9608;&#9608;&#9608;</FONT></TD><TD>Lower Leg</TD></TR>" -f $bodyPartColor["lower_leg"]))
$lines.Add("    </TABLE>")
$lines.Add("  >];")
$lines.Add("  { rank=min; Legend }")

$lines.Add("}")

$outDir = Split-Path -Parent $OutDot
if ($outDir -and -not (Test-Path $outDir)) {
  New-Item -ItemType Directory -Path $outDir | Out-Null
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllLines($OutDot, $lines, $utf8NoBom)
Write-Output "Wrote DOT: $OutDot"

$dotExe = Get-Command dot -ErrorAction SilentlyContinue
if ($dotExe) {
  & $dotExe.Source -Tsvg $OutDot -o $OutSvg
  Write-Output "Wrote SVG: $OutSvg"
}
else {
  Write-Warning "Graphviz dot not found in PATH."
}
