// Small CPU BVH used by rqf_metrics.py; no external geometry dependencies.
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>
#include <numeric>
#include <vector>

struct Vec {
    double x[3];
    Vec operator-(const Vec& b) const { return {{x[0]-b.x[0],x[1]-b.x[1],x[2]-b.x[2]}}; }
    double dot(const Vec& b) const {return x[0]*b.x[0]+x[1]*b.x[1]+x[2]*b.x[2];}
    Vec cross(const Vec& b) const {return {{x[1]*b.x[2]-x[2]*b.x[1],x[2]*b.x[0]-x[0]*b.x[2],x[0]*b.x[1]-x[1]*b.x[0]}};}
};
struct Triangle { Vec a,b,c,center; };
struct Node { Vec lo,hi; int begin,end,left=-1,right=-1; };
struct BVH {
    std::vector<Triangle> triangles;
    std::vector<int> order;
    std::vector<Node> nodes;
    int build(int begin,int end) {
        Node n; n.begin=begin;n.end=end;
        for(int a=0;a<3;a++){n.lo.x[a]=std::numeric_limits<double>::infinity();n.hi.x[a]=-n.lo.x[a];}
        for(int j=begin;j<end;j++) {
            const Triangle& t=triangles[order[j]];
            for(int a=0;a<3;a++) {
                n.lo.x[a]=std::min(n.lo.x[a],std::min(t.a.x[a],std::min(t.b.x[a],t.c.x[a])));
                n.hi.x[a]=std::max(n.hi.x[a],std::max(t.a.x[a],std::max(t.b.x[a],t.c.x[a])));
            }
        }
        int index=(int)nodes.size();nodes.push_back(n);
        if(end-begin>8) {
            int axis=0;for(int a=1;a<3;a++)if(n.hi.x[a]-n.lo.x[a]>n.hi.x[axis]-n.lo.x[axis])axis=a;
            int middle=(begin+end)/2;
            std::nth_element(order.begin()+begin,order.begin()+middle,order.begin()+end,
                [&](int a,int b){return triangles[a].center.x[axis]<triangles[b].center.x[axis];});
            int left=build(begin,middle),right=build(middle,end);
            nodes[index].left=left;nodes[index].right=right;
        }
        return index;
    }
    bool box(const Node& n,const Vec& origin,const Vec& direction,double best) const {
        double near=0,far=best;
        for(int a=0;a<3;a++) {
            if(std::abs(direction.x[a])<1e-15) {
                if(origin.x[a]<n.lo.x[a]-1e-12||origin.x[a]>n.hi.x[a]+1e-12)return false;
            } else {
                double first=(n.lo.x[a]-origin.x[a])/direction.x[a],second=(n.hi.x[a]-origin.x[a])/direction.x[a];
                if(first>second)std::swap(first,second);
                near=std::max(near,first);far=std::min(far,second);
                if(near>far+1e-12)return false;
            }
        }
        return true;
    }
    double cast(const Vec& origin,const Vec& direction,double limit) const {
        double best=limit;bool found=false;
        std::vector<int> stack;stack.reserve(64);stack.push_back(0);
        while(!stack.empty()) {
            int index=stack.back();stack.pop_back();const Node& n=nodes[index];
            if(!box(n,origin,direction,best))continue;
            if(n.left>=0){stack.push_back(n.left);stack.push_back(n.right);continue;}
            for(int j=n.begin;j<n.end;j++) {
                const Triangle& t=triangles[order[j]];
                Vec e1=t.b-t.a,e2=t.c-t.a,p=direction.cross(e2);
                double det=e1.dot(p);if(std::abs(det)<1e-14)continue;
                Vec s=origin-t.a;double u=s.dot(p)/det;if(u < -1e-9 || u > 1+1e-9)continue;
                Vec q=s.cross(e1);double v=direction.dot(q)/det;if(v < -1e-9 || u+v > 1+1e-9)continue;
                double distance=e2.dot(q)/det;
                if(distance>1e-10&&distance<=best){best=distance;found=true;}
            }
        }
        return found?best:std::numeric_limits<double>::infinity();
    }
};
extern "C" {
void* rqf_bvh_create(const double* vertices,const int32_t* faces,int count) {
    try {
        BVH* b=new BVH;b->triangles.resize(count);b->order.resize(count);
        std::iota(b->order.begin(),b->order.end(),0);
        for(int i=0;i<count;i++) {
            Triangle& t=b->triangles[i];
            for(int a=0;a<3;a++) {
                t.a.x[a]=vertices[3*faces[3*i]+a];t.b.x[a]=vertices[3*faces[3*i+1]+a];t.c.x[a]=vertices[3*faces[3*i+2]+a];
                t.center.x[a]=(t.a.x[a]+t.b.x[a]+t.c.x[a])/3;
            }
        }
        b->build(0,count);return b;
    }catch(...){return nullptr;}
}
void rqf_bvh_destroy(void* handle){delete static_cast<BVH*>(handle);}
void rqf_bvh_cast(void* handle,const double* origins,const double* targets,int count,double* distances) {
    BVH* b=static_cast<BVH*>(handle);
    for(int i=0;i<count;i++) {
        Vec origin={{origins[3*i],origins[3*i+1],origins[3*i+2]}};
        Vec target={{targets[3*i],targets[3*i+1],targets[3*i+2]}};
        Vec d=target-origin;double length=std::sqrt(d.dot(d));
        if(length<1e-12){distances[i]=std::numeric_limits<double>::infinity();continue;}
        for(int a=0;a<3;a++)d.x[a]/=length;
        distances[i]=b->cast(origin,d,length+1e-3*std::max(1.0,length));
    }
}
}
