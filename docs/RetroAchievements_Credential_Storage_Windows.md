# WindowsでのRetroAchievements認証情報の保存

## 概要

Windowsでは、認証情報の保存には **Windows Credential
Manager（資格情報マネージャー）** を利用するのが一般的です。

OS標準のAPIを利用することで、安全にユーザー名やパスワード、アクセストークンを保存できます。

------------------------------------------------------------------------

## 推奨する保存方法

  方法                             推奨度  備考
  ------------------------------- -------- ----------------
  Windows Credential Manager       ★★★★★   OS標準・推奨
  DPAPIで暗号化した設定ファイル    ★★★★☆   フォールバック
  平文の設定ファイル               ★☆☆☆☆   非推奨

------------------------------------------------------------------------

## Credential Manager の利用

保存

``` cpp
CREDENTIALW cred = {};
cred.Type = CRED_TYPE_GENERIC;
cred.TargetName = L"org.bubiboy.RetroAchievements:username";
cred.UserName = L"username";
cred.CredentialBlob = (LPBYTE)password;
cred.CredentialBlobSize = passwordLength;

CredWriteW(&cred, 0);
```

取得

``` cpp
PCREDENTIALW cred = nullptr;
CredReadW(
    L"org.bubiboy.RetroAchievements:username",
    CRED_TYPE_GENERIC,
    0,
    &cred);

// 利用後
CredFree(cred);
```

------------------------------------------------------------------------

## 保存する情報

-   ユーザー名
-   パスワード（またはアクセストークン）
-   APIキー（必要な場合）

例

``` text
Target:
    org.bubiboy.RetroAchievements:username

User:
    username

Password:
    ********
```

------------------------------------------------------------------------

## フォールバック

Credential Managerが利用できない特殊な環境では、

1.  DPAPI（CryptProtectData）
2.  暗号化した設定ファイル

という方法が利用できます。

------------------------------------------------------------------------

## クロスプラットフォーム設計

``` cpp
CredentialStore::save(service, account, password);
CredentialStore::load(service, account);
```

内部実装

-   macOS → Keychain
-   Windows → Credential Manager
-   Linux → Secret Service（libsecret）
-   フォールバック → 暗号化または権限を制限した設定ファイル
