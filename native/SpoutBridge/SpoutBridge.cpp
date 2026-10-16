// SpoutDX を C ABI で公開する薄いブリッジ。.NET から P/Invoke で呼ぶ。
#include "SpoutDX.h"

#define BRIDGE_API extern "C" __declspec(dllexport)

// device: 呼び出し側が所有する ID3D11Device。送信者の生存中は有効であること
BRIDGE_API void* SpoutBridge_Create(const char* senderName, ID3D11Device* device)
{
    auto* spout = new spoutDX();
    if (!spout->OpenDirectX11(device) || !spout->SetSenderName(senderName))
    {
        spout->CloseDirectX11();
        delete spout;
        return nullptr;
    }
    return spout;
}

// texture: device 上の BGRA8 テクスチャ。共有テクスチャへコピーして送信する
BRIDGE_API bool SpoutBridge_SendTexture(void* handle, ID3D11Texture2D* texture)
{
    if (!handle || !texture) return false;
    return static_cast<spoutDX*>(handle)->SendTexture(texture);
}

BRIDGE_API void SpoutBridge_Destroy(void* handle)
{
    if (!handle) return;
    auto* spout = static_cast<spoutDX*>(handle);
    spout->ReleaseSender();
    spout->CloseDirectX11();
    delete spout;
}
