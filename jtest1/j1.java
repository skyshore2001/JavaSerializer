import java.io.*;

import java.io.Serializable;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedList;
import java.util.List;
import java.util.Map;

class Data1 implements Serializable {
    private static final long serialVersionUID = 18001L;

    int ival;
    String name;
    byte[] buf;
    double[] doubleArr;
    List<int[]> X;
    List<float[]> Y;
    Map<String, Boolean> map;
}

public class j1
{
    public static void main(String[] args) throws Exception {
    	Data1 d = new Data1();
    	d.ival = 0x99;
    	d.name = "TEST-DATA1";
    	d.buf = new byte[] { 0x41, 0x42, 0x43, 0x44 }; // "ABCD"
    	d.doubleArr = new double[] {1.1, 2.2, 3.3, 4.4};
    	
    	d.X = new ArrayList<int[]>();
    	d.X.add(new int[] {0x61626364, 0x65666768}); // "abcd", "efgh"
    	d.X.add(new int[] {0x696a6b6c, 0x6d6e6f70}); // "ijkl", "mnop"
    	
    	d.Y = new LinkedList<float[]>();
    	d.Y.add(new float[] {11.1f, 22.2f, 33.3f, 44.4f});
    	d.Y.add(new float[] {55.5f, 66.6f});
    	
    	d.map = new HashMap<>();
    	d.map.put("key1", true);
    	d.map.put("key2", false);
    	d.map.put("key3", true);

    	String fname = "test1.bin";
    	try (FileOutputStream fout = new FileOutputStream(fname);
    		ObjectOutputStream output = new ObjectOutputStream(fout)) {
	        // 写入int:
	        //output.writeInt(0x12345678);
	        // 写入String:
	        //output.writeBytes("hello1");
	        //output.writeUTF("hello2");
	        // 写入Object:
	        output.writeObject(d);
    	}
    	
    	try (FileInputStream fin = new FileInputStream(fname); 
    			ObjectInputStream oin = new ObjectInputStream(fin)) {
	        Object o = oin.readObject();
	        System.out.println(o);
    	}
    }
}
