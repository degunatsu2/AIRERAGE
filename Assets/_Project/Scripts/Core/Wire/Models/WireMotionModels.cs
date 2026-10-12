using System;

namespace Degunatsu.Core.Wire.Models
{
    /// <summary>
    /// WireModelType から運動モデルを取り出す。モデルは状態を持たないので、インスタンスを共有する。
    /// </summary>
    public static class WireMotionModels
    {
        private static readonly IWireMotionModel InertialModel = new InertialWireModel();
        private static readonly IWireMotionModel ResponsiveModel = new ResponsiveWireModel();

        public static IWireMotionModel Get(WireModelType type)
        {
            switch (type)
            {
                case WireModelType.Inertial:
                    return InertialModel;
                case WireModelType.Responsive:
                    return ResponsiveModel;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "未対応のワイヤーモデルです");
            }
        }
    }
}
