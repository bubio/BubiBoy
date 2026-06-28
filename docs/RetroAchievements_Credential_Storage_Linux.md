# LinuxでのRetroAchievements認証情報の保存

## 概要

macOSではKeychain、WindowsではCredential
Managerを利用するのが一般的です。
Linuxでは、OSが提供するシークレットストレージを利用するのが定番です。

平文で設定ファイルにパスワードを保存する方法は、現在のデスクトップアプリではあまり推奨されません。

------------------------------------------------------------------------

# Linuxでの定番

  環境                 推奨
  -------------------- ---------------------------------------------
  GNOME                Secret Service（libsecret / GNOME Keyring）
  KDE Plasma           KWallet
  その他デスクトップ   Secret Service API（KeePassXCも対応可能）
  CLI・サーバー        設定ファイル（権限600）または環境変数

------------------------------------------------------------------------

# おすすめは Secret Service

Secret Service API
に対応しておけば、多くのLinuxデスクトップ環境で利用できます。

対応例

-   GNOME Keyring
-   KWallet（Secret Serviceプラグイン）
-   KeePassXC
-   その他 Secret Service 対応ストレージ

アプリ側は **libsecret** を利用するのが一般的です。

------------------------------------------------------------------------

# C/C++での実装

保存

``` cpp
secret_password_store_sync(
    schema,
    SECRET_COLLECTION_DEFAULT,
    "RetroAchievements",
    password,
    nullptr,
    &error,
    "user", username,
    nullptr);
```

取得

``` cpp
secret_password_lookup_sync(...)
```

------------------------------------------------------------------------

# OSごとの保存先

  OS        保存先
  --------- -----------------------------
  macOS     Keychain
  Windows   Credential Manager
  Linux     Secret Service（libsecret）

------------------------------------------------------------------------

# RetroAchievementsで保存する情報

通常は以下の情報のみで十分です。

-   ユーザー名
-   パスワード（または将来的なアクセストークン）
-   APIキー（必要になった場合）

イメージ

``` text
Service:
    RetroAchievements

Account:
    username

Password:
    ********
```

------------------------------------------------------------------------

# フォールバック

LinuxではGUI環境が存在しない場合があります。

そのため、多くのアプリでは以下のようなフォールバックを用意しています。

1.  Secret Service が利用できればそちらへ保存
2.  利用できなければ `~/.config/<AppName>/config.ini` に保存
3.  設定ファイルの権限を `600` に設定

WSLや最小構成のLinuxではSecret Serviceが存在しないこともあります。

------------------------------------------------------------------------

# クロスプラットフォーム設計

認証情報の保存は抽象化すると保守しやすくなります。

``` cpp
CredentialStore::save(service, account, password);
CredentialStore::load(service, account);
```

内部実装

-   macOS → Keychain
-   Windows → Credential Manager
-   Linux → Secret Service（libsecret）
-   利用できない場合 → 設定ファイル（権限600）

これにより、OSごとの違いをアプリケーション本体から切り離すことができます。
