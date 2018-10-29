// Generated by SpatialOS codegen. DO NOT EDIT!
// source: improbable.WorkerAttributeSet in improbable/standard_library.schema.

namespace Improbable
{

public partial struct WorkerAttributeSet : global::System.IEquatable<WorkerAttributeSet>, global::Improbable.Collections.IDeepCopyable<WorkerAttributeSet>
{
  /// <summary>
  /// Field attribute = 1.
  /// </summary>
  public global::Improbable.Collections.List<string> attribute;

  public WorkerAttributeSet(global::Improbable.Collections.List<string> attribute)
  {
    this.attribute = attribute;
  }

  public static WorkerAttributeSet Create()
  {
    var _result = new WorkerAttributeSet();
    _result.attribute = new global::Improbable.Collections.List<string>();
    return _result;
  }

  public WorkerAttributeSet DeepCopy()
  {
    var _result = new WorkerAttributeSet();
    _result.attribute = attribute.DeepCopy();
    return _result;

  }

  public override bool Equals(object _obj)
  {
    return _obj is WorkerAttributeSet && Equals((WorkerAttributeSet) _obj);
  }

  public static bool operator==(WorkerAttributeSet a, WorkerAttributeSet b)
  {
    return a.Equals(b);
  }

  public static bool operator!=(WorkerAttributeSet a, WorkerAttributeSet b)
  {
    return !a.Equals(b);
  }

  public bool Equals(WorkerAttributeSet _obj)
  {
    return
        attribute == _obj.attribute;
  }

  public override int GetHashCode()
  {
    int _result = 1327;
    _result = (_result * 977) + (attribute == null ? 0 : attribute.GetHashCode());
    return _result;
  }
}

public static class WorkerAttributeSet_Internal
{
  public static unsafe void Write(global::Improbable.Worker.Internal.GcHandlePool _pool,
                                  WorkerAttributeSet _data, global::Improbable.Worker.Internal.Pbio.Object* _obj)
  {
    for (int _i = 0; _i < _data.attribute.Count; ++_i)
    {
      if (_data.attribute[_i] != null)
      {
        var _buffer = global::System.Text.Encoding.UTF8.GetBytes(_data.attribute[_i]);
        global::Improbable.Worker.Internal.Pbio.AddBytes(_obj, 1, (byte*) _pool.Pin(_buffer), (uint) _buffer.Length);
      }
      else{
        global::Improbable.Worker.Internal.Pbio.AddBytes(_obj, 1, null, 0);
      }
    }
  }

  public static unsafe WorkerAttributeSet Read(global::Improbable.Worker.Internal.Pbio.Object* _obj)
  {
    WorkerAttributeSet _data;
    {
      var _count = global::Improbable.Worker.Internal.Pbio.GetBytesCount(_obj, 1);
      _data.attribute = new global::Improbable.Collections.List<string>((int) _count);
      for (uint _i = 0; _i < _count; ++_i)
      {
        _data.attribute.Add(global::System.Text.Encoding.UTF8.GetString(global::Improbable.Worker.Bytes.CopyOf(global::Improbable.Worker.Internal.Pbio.IndexBytes(_obj, 1, _i), global::Improbable.Worker.Internal.Pbio.IndexBytesLength(_obj, 1, _i)).BackingArray));
      }
    }
    return _data;
  }
}

}
