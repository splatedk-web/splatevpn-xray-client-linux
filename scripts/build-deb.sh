#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
stage="$(mktemp -d /tmp/splate-deb.XXXXXX)"
trap 'rm -rf -- "$stage"' EXIT
test -x dist/linux-x64/SplateClient
mkdir -p "$stage/DEBIAN" "$stage/opt/splatevpn" "$stage/usr/libexec" "$stage/usr/share/applications" "$stage/usr/share/icons/hicolor/256x256/apps" "$stage/usr/share/polkit-1/actions"
cp -a dist/linux-x64/. "$stage/opt/splatevpn/"
install -m755 scripts/splatevpn-network "$stage/usr/libexec/splatevpn-network"
install -m644 scripts/io.splatevpn.network.policy "$stage/usr/share/polkit-1/actions/"
install -m644 scripts/splatevpn.desktop "$stage/usr/share/applications/"
install -m644 Assets/app.png "$stage/usr/share/icons/hicolor/256x256/apps/splatevpn.png"
chmod 755 "$stage/opt/splatevpn/SplateClient" "$stage/opt/splatevpn/core/xray" "$stage/opt/splatevpn/core/xray.bin" "$stage/opt/splatevpn/core/tun2socks"
cat > "$stage/DEBIAN/control" <<'EOF'
Package: splatevpn
Version: 2.14.1-1
Section: net
Priority: optional
Architecture: amd64
Maintainer: SplateVPN
Depends: libc6 (>= 2.38), libstdc++6 (>= 13), gsettings-desktop-schemas, libwebkit2gtk-4.1-0, libgtk-3-0 | libgtk-3-0t64, libnotify4, libayatana-appindicator3-1, iproute2, python3, policykit-1 | polkitd, pkexec, libcap2-bin, zenity, xclip, wl-clipboard, zbar-tools, wireless-tools
Recommends: gnome-screenshot
Description: SplateVPN Linux desktop client
 Native WebKitGTK interface, Xray, automatic server balancing,
 XDG autostart and a polkit TUN helper preserving inbound replies.
EOF
cat > "$stage/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -eu
if [ "$1" = configure ]; then
 setcap cap_net_raw+ep /opt/splatevpn/core/xray.bin
fi
EOF
chmod 755 "$stage/DEBIAN/postinst"
# A checkout on Windows/DrvFS can report every source file as mode 0777.
# Normalize the staged package before installing any capability-bearing binary.
find "$stage" -type d -exec chmod 755 {} +
find "$stage" -type f -exec chmod 644 {} +
chmod 755 "$stage/DEBIAN/postinst" "$stage/usr/libexec/splatevpn-network" "$stage/opt/splatevpn/SplateClient" "$stage/opt/splatevpn/core/xray" "$stage/opt/splatevpn/core/xray.bin" "$stage/opt/splatevpn/core/tun2socks"
dpkg-deb --root-owner-group --build "$stage" dist/splatevpn_2.14.1-1_amd64.deb
